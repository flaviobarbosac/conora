using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

/// <summary>
/// WhatsApp Cloud API integration. Text launches are guided in a conversation until the person confirms each field.
/// Budget questions are answered without writing anything. Receipt photos persist when OCR/classification is ready.
/// </summary>
public sealed partial class WhatsAppService
{
    private static readonly string[] IncomeWords = ["recebi", "receita", "salário", "salario", "ganhei", "entrou"];
    private static readonly string[] CancelWords = ["cancelar", "cancela", "desistir", "desisto", "parar", "para", "esquece"];
    private static readonly string[] YesWords = ["sim", "s", "ok", "certo", "isso", "confirmo", "confirma", "pode", "fechado", "isso mesmo"];
    private static readonly string[] NoWords = ["não", "nao", "n", "outro", "outra", "mudar", "alterar", "errado", "não é", "nao e"];
    private static readonly JsonSerializerOptions SessionJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly IFinanceRepository _repo;
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenant;
    private readonly ICorrelationContext _correlation;
    private readonly PlanService _plan;
    private readonly EntryService _entries;
    private readonly BudgetService _budgets;
    private readonly PendingDebtService _debts;
    private readonly AccountService _accounts;
    private readonly IGeminiClient _gemini;
    private readonly IWhatsAppMessenger _messenger;
    private readonly ReceiptIntakeService _receipts;

    public WhatsAppService(
        IFinanceRepository repo,
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ITenantContext tenant,
        ICorrelationContext correlation,
        PlanService plan,
        EntryService entries,
        BudgetService budgets,
        PendingDebtService debts,
        AccountService accounts,
        IGeminiClient gemini,
        IWhatsAppMessenger messenger,
        ReceiptIntakeService receipts)
    {
        _repo = repo;
        _audits = audits;
        _uow = uow;
        _tenant = tenant;
        _correlation = correlation;
        _plan = plan;
        _entries = entries;
        _budgets = budgets;
        _debts = debts;
        _accounts = accounts;
        _gemini = gemini;
        _messenger = messenger;
        _receipts = receipts;
    }

    public Task<WhatsAppPairingResponse> IssuePairingCodeAsync(CancellationToken ct)
    {
        var userId = _tenant.UsuarioId ?? throw new UnauthorizedAccessException("Tenant não autenticado.");
        var code = WhatsAppPairingCodes.Issue(userId);
        return Task.FromResult(new WhatsAppPairingResponse(code, $"CONORA {code}"));
    }

    public async Task<WhatsAppLinkResponse?> GetLinkAsync(CancellationToken ct)
    {
        var link = await _repo.FirstOrDefaultAsync<WhatsAppLink>(_ => true, ct, track: false);
        return link is null ? null : new WhatsAppLinkResponse(link.PhoneE164, link.LinkedAt);
    }

    public async Task<WhatsAppLinkResponse> LinkAsync(LinkWhatsAppRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var phone = WhatsAppLink.NormalizePhone(request.Phone);

        var owner = await _repo.FirstOrDefaultGlobalByKeyAsync<WhatsAppLink>(l => l.PhoneE164 == phone, ct);
        if (owner is not null && owner.UsuarioId != _tenant.UsuarioId)
            throw new ValidationException("phone", "Este telefone já está vinculado a outra conta.");

        var current = await _repo.ListAsync<WhatsAppLink>(null, ct, track: true);
        foreach (var old in current)
            _repo.SoftDelete(old);

        var link = WhatsAppLink.Create(phone);
        _repo.Add(link);
        AuditRecorder.Record(_audits, _correlation, "WhatsAppLink", link.Id, "WhatsAppLinked", new { link.PhoneE164 });
        await _uow.SaveChangesAsync(ct);
        return new WhatsAppLinkResponse(link.PhoneE164, link.LinkedAt);
    }

    public async Task UnlinkAsync(CancellationToken ct)
    {
        var current = await _repo.ListAsync<WhatsAppLink>(null, ct, track: true);
        foreach (var link in current)
            _repo.SoftDelete(link);

        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>Handles a Cloud API webhook body, creates entries when needed, and sends WhatsApp replies.</summary>
    public async Task HandleWebhookAsync(string body, CancellationToken ct)
    {
        foreach (var message in WhatsAppInbound.Extract(body))
        {
            WhatsAppReply? reply = message.Kind switch
            {
                WhatsAppInbound.Kind.Text => await HandleMessageAsync(message.From, message.Text!, ct),
                WhatsAppInbound.Kind.Image => await HandleImageAsync(message.From, message.MediaId!, ct),
                WhatsAppInbound.Kind.Audio => await HandleAudioAsync(message.From, message.MediaId!, ct),
                _ => null
            };
            if (reply is null)
                continue;

            try
            {
                await _messenger.SendTextAsync(reply.PhoneE164, reply.Text, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Reply delivery must not fail the webhook ack to Meta.
            }
        }
    }

    public async Task<WhatsAppReply?> HandleAudioAsync(string fromDigits, string mediaId, CancellationToken ct)
    {
        var phone = WhatsAppLink.NormalizePhone(fromDigits);
        var link = await _repo.FirstOrDefaultGlobalByKeyAsync<WhatsAppLink>(l => l.PhoneE164 == phone, ct);
        if (link is null)
            return new WhatsAppReply(phone, "Este número ainda não está vinculado. Abra o Conora, toque em Abrir WhatsApp e envie o código.");

        _tenant.Set(link.UsuarioId);

        if (!_gemini.IsConfigured)
            return new WhatsAppReply(phone, "Ainda não consigo ouvir áudio por aqui. Manda em texto, por favor.");

        try
        {
            var media = await _messenger.DownloadMediaAsync(mediaId, ct);
            if (media is null)
                return new WhatsAppReply(phone, "Não consegui baixar o áudio. Tente de novo ou mande em texto.");

            var mime = string.IsNullOrWhiteSpace(media.Value.MimeType)
                ? "audio/ogg"
                : media.Value.MimeType.Split(';', 2)[0].Trim();
            var transcript = await _gemini.TranscribeAudioAsync(media.Value.Bytes, mime, ct);
            if (string.IsNullOrWhiteSpace(transcript))
                return new WhatsAppReply(phone, "Não entendi o áudio. Pode repetir ou mandar em texto?");

            return await HandleMessageAsync(fromDigits, transcript.Trim(), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new WhatsAppReply(phone, "Não consegui processar o áudio. Tente de novo em instantes.");
        }
    }

    public async Task<WhatsAppReply?> HandleImageAsync(string fromDigits, string mediaId, CancellationToken ct)
    {
        var phone = WhatsAppLink.NormalizePhone(fromDigits);
        var link = await _repo.FirstOrDefaultGlobalByKeyAsync<WhatsAppLink>(l => l.PhoneE164 == phone, ct);
        if (link is null)
            return new WhatsAppReply(phone, "Este número ainda não está vinculado. Abra o Conora, toque em Abrir WhatsApp e envie o código.");

        _tenant.Set(link.UsuarioId);

        try
        {
            await _plan.EnsureWritableAsync(ct);
            var media = await _messenger.DownloadMediaAsync(mediaId, ct);
            if (media is null)
                return new WhatsAppReply(phone, "Não consegui baixar a foto do cupom. Tente de novo.");

            var intake = await _receipts.CreateFromBytesAsync(media.Value.Bytes, media.Value.MimeType, ReceiptChannel.WhatsApp, ct, phone);

            // Wait briefly for background OCR (same process).
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(500, ct);
                intake = await _receipts.GetAsync(intake.Id, ct);
                if (intake.Status is not ReceiptIntakeStatus.Processing)
                    break;
            }

            if (intake.Status == ReceiptIntakeStatus.Failed)
                return new WhatsAppReply(phone, intake.FailureReason ?? "Não consegui ler o cupom.");

            if (intake.MissingRequired is { Count: > 0 })
                return new WhatsAppReply(phone, ReceiptPhotoCopy.ManualQuestion + "\n1 = Preencher agora\n2 = Enviar outra foto");

            if (intake.Status == ReceiptIntakeStatus.Confirmed)
                return new WhatsAppReply(phone, BuildPersistedReceiptReply(intake));

            if (intake.NeedsClassification || intake.Status == ReceiptIntakeStatus.NeedsClassification)
                return new WhatsAppReply(phone, await BuildClassificationListAsync(intake.ExtractedName, ct));

            return await PersistReadyReceiptAsync(intake, phone, ct);
        }
        catch (PlanReadOnlyException ex)
        {
            return new WhatsAppReply(phone, ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new WhatsAppReply(phone, "Não consegui processar a foto. Tente de novo em instantes.");
        }
    }

    public async Task<WhatsAppReply?> HandleMessageAsync(string fromDigits, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var phone = WhatsAppLink.NormalizePhone(fromDigits);
        var pairedUser = WhatsAppPairingCodes.TryConsume(text);
        if (pairedUser is Guid userId)
        {
            _tenant.Set(userId);
            try
            {
                await LinkAsync(new LinkWhatsAppRequest(phone), ct);
                return new WhatsAppReply(phone, "Pronto. Este WhatsApp está conectado ao Conora. Pode mandar suas mensagens.");
            }
            catch (ValidationException ex)
            {
                var detail = ex.Errors.SelectMany(pair => pair.Value).FirstOrDefault() ?? ex.Message;
                return new WhatsAppReply(phone, detail);
            }
        }

        var link = await _repo.FirstOrDefaultGlobalByKeyAsync<WhatsAppLink>(l => l.PhoneE164 == phone, ct);
        if (link is null)
            return new WhatsAppReply(phone, "Este número ainda não está vinculado. Abra o Conora, toque em Abrir WhatsApp e envie o código.");

        _tenant.Set(link.UsuarioId);

        var activeReceipt = await _receipts.FindActiveWhatsAppAsync(phone, ct);
        if (activeReceipt is not null)
        {
            var receiptReply = await HandleReceiptReplyAsync(activeReceipt, text.Trim(), ct);
            if (receiptReply is not null)
                return receiptReply;
        }

        var trimmed = text.Trim();
        if (IsCancel(trimmed))
        {
            await DiscardSessionAsync(phone, ct);
            return new WhatsAppReply(phone, "Tudo bem, cancelei essa conversa. Quando quiser lançar de novo, é só mandar.");
        }

        if (WhatsAppInbound.IsHelp(trimmed))
            return new WhatsAppReply(phone, BuildHelpReply());

        if (WhatsAppInbound.IsQueryIntent(trimmed))
            return new WhatsAppReply(phone, await AnswerQueryAsync(trimmed, ct));

        try
        {
            var session = await FindPendingSessionAsync(phone, ct);
            if (session is not null)
                return await ContinueConversationAsync(session.Value.Draft, session.Value.State, trimmed, phone, ct);

            await _plan.EnsureWritableAsync(ct);
            return await StartConversationAsync(phone, trimmed, ct);
        }
        catch (PlanReadOnlyException ex)
        {
            return new WhatsAppReply(phone, ex.Message);
        }
        catch (ValidationException ex)
        {
            var detail = ex.Errors.SelectMany(pair => pair.Value).FirstOrDefault() ?? ex.Message;
            return new WhatsAppReply(phone, $"Não consegui seguir com o lançamento: {detail}");
        }
        catch (DomainException ex)
        {
            return new WhatsAppReply(phone, $"Não consegui seguir com o lançamento: {ex.Message}");
        }
        catch (Exception)
        {
            return new WhatsAppReply(phone, "Não consegui seguir com o lançamento. Tente de novo em instantes.");
        }
    }

    private async Task<WhatsAppReply?> HandleReceiptReplyAsync(ReceiptIntake intake, string text, CancellationToken ct)
    {
        var phone = intake.PhoneE164 ?? "";
        if (intake.WhatsAppAwaiting == WhatsAppReceiptAwaiting.ManualChoice)
        {
            if (text is "1" or "sim" or "Sim" or "SIM")
            {
                var updated = await _receipts.AcceptManualFillAsync(intake.Id, ct);
                if (updated.MissingRequired is { Count: > 0 })
                    return new WhatsAppReply(phone,
                        "Anotei o que deu para ler. Abra o Conora e complete o que ficou em branco, inclusive a conta e a categoria.");
                if (updated.NeedsClassification || updated.Status == ReceiptIntakeStatus.NeedsClassification)
                    return new WhatsAppReply(phone, await BuildClassificationListAsync(updated.ExtractedName, ct));
                return await PersistReadyReceiptAsync(updated, phone, ct);
            }

            if (text is "2" or "não" or "nao" or "Não" or "Nao" or "NAO")
            {
                await _receipts.DeclineManualFillAsync(intake.Id, ct);
                return new WhatsAppReply(phone, ReceiptPhotoCopy.ClearerPhoto);
            }

            return new WhatsAppReply(phone, ReceiptPhotoCopy.ManualQuestion + "\n1 = Preencher agora\n2 = Enviar outra foto");
        }

        if (intake.WhatsAppAwaiting == WhatsAppReceiptAwaiting.Classification)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index < 1)
                return new WhatsAppReply(phone, "Responda com o número da conta da lista.");

            var categories = await _receipts.ListClassifiableCategoriesAsync(ct);
            if (index > categories.Count)
                return new WhatsAppReply(phone, "Número inválido. Escolha um da lista.");

            var chosen = categories[index - 1];
            if (string.IsNullOrWhiteSpace(chosen.Code))
                return new WhatsAppReply(phone, "Essa conta não tem código de sistema. Escolha outra.");

            var updated = await _receipts.ClassifyAsync(intake.Id, new ClassifyReceiptRequest(chosen.Code), ct);
            if (updated.Status == ReceiptIntakeStatus.Confirmed)
                return new WhatsAppReply(phone, BuildPersistedReceiptReply(updated));
            return await PersistReadyReceiptAsync(updated, phone, ct);
        }

        return null;
    }

    private async Task<WhatsAppReply> PersistReadyReceiptAsync(
        ReceiptIntakeResponse intake,
        string phone,
        CancellationToken ct)
    {
        try
        {
            var confirmed = await _receipts.ConfirmAsync(intake.Id, new ConfirmReceiptRequest(), ct);
            return new WhatsAppReply(phone,
                $"Lançamento gravado: R$ {confirmed.Amount.ToString("N2", new CultureInfo("pt-BR"))} ({confirmed.Description}).");
        }
        catch (ValidationException ex)
        {
            var detail = ex.Errors.SelectMany(pair => pair.Value).FirstOrDefault() ?? ex.Message;
            return new WhatsAppReply(phone, $"Não consegui gravar o cupom: {detail}");
        }
        catch (DomainException ex)
        {
            return new WhatsAppReply(phone, $"Não consegui gravar o cupom: {ex.Message}");
        }
    }

    private static string BuildPersistedReceiptReply(ReceiptIntakeResponse intake)
    {
        var amount = (intake.ExtractedAmount ?? 0).ToString("N2", new CultureInfo("pt-BR"));
        return $"Lançamento gravado: R$ {amount} ({intake.ExtractedName ?? "recibo"}).";
    }

    private async Task<string> BuildClassificationListAsync(string? supplierName, CancellationToken ct)
    {
        var categories = await _receipts.ListClassifiableCategoriesAsync(ct);
        var br = new CultureInfo("pt-BR");
        var header = string.IsNullOrWhiteSpace(supplierName)
            ? "Qual conta do plano classifica este fornecedor? Responda com o número:"
            : $"Fornecedor: {supplierName}. Qual conta classifica? Responda com o número:";
        var lines = categories
            .Take(40)
            .Select((c, i) => $"{i + 1}. {c.DisplayNumber ?? c.Code} {c.Name}");
        return header + "\n" + string.Join("\n", lines);
    }

    private async Task<WhatsAppParsedEntry?> ParseEntryAsync(string text, CancellationToken ct)
    {
        var fromAi = await TryParseWithGeminiAsync(text, ct);
        return fromAi ?? ParseWithRules(text);
    }

    private async Task<WhatsAppParsedEntry?> TryParseWithGeminiAsync(string text, CancellationToken ct)
    {
        if (!_gemini.IsConfigured)
            return null;

        const string system =
            "Extraia um lançamento financeiro de uma mensagem em português. Responda estritamente em JSON: " +
            "{\"type\":\"expense\" ou \"income\",\"amount\":número positivo,\"description\":\"texto curto\"}. " +
            "Se não houver valor, responda {\"amount\":0}. Não dê conselhos.";
        var raw = await _gemini.GenerateJsonAsync(system, text, ct);
        if (raw is null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("amount", out var amountEl) || !amountEl.TryGetDecimal(out var amount) || amount <= 0 || amount > 99_999_999.99m)
                return null;

            var income = root.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "income";
            var description = root.TryGetProperty("description", out var d) ? d.GetString() : null;
            return new WhatsAppParsedEntry(
                income ? EntryType.Income : EntryType.Expense,
                decimal.Round(amount, 2),
                string.IsNullOrWhiteSpace(description) ? text.Trim() : description.Trim(),
                DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static WhatsAppParsedEntry? ParseWithRules(string text)
    {
        var match = AmountRegex().Match(text);
        if (!match.Success)
            return null;

        var amount = ImportParser.ParseAmount(match.Value);
        if (amount is null or <= 0 or > 99_999_999.99m)
            return null;

        var lowered = text.ToLowerInvariant();
        var type = IncomeWords.Any(lowered.Contains) ? EntryType.Income : EntryType.Expense;
        var description = (text[..match.Index] + " " + text[(match.Index + match.Length)..]).Trim();
        description = Regex.Replace(description, @"\s+", " ");
        return new WhatsAppParsedEntry(type, amount.Value, string.IsNullOrWhiteSpace(description) ? text.Trim() : description, DateTime.UtcNow);
    }

    [GeneratedRegex(@"\d{1,3}(?:\.\d{3})+(?:,\d{1,2})?|\d+(?:[.,]\d{1,2})?")]
    private static partial Regex AmountRegex();
}
