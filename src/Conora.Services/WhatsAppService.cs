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
/// WhatsApp Cloud API integration. Incoming text creates an entry immediately and replies with success or error.
/// Budget questions are answered without writing anything.
/// </summary>
public sealed partial class WhatsAppService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
    private static readonly string[] QueryWords = ["orçamento", "orcamento", "saldo", "resumo", "quanto posso", "quanto sobrou"];
    private static readonly string[] IncomeWords = ["recebi", "receita", "salário", "salario", "ganhei", "entrou"];

    private readonly IFinanceRepository _repo;
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenant;
    private readonly ICorrelationContext _correlation;
    private readonly PlanService _plan;
    private readonly EntryService _entries;
    private readonly BudgetService _budgets;
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
        _gemini = gemini;
        _messenger = messenger;
        _receipts = receipts;
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

    public async Task<IReadOnlyList<WhatsAppDraftResponse>> ListDraftsAsync(WhatsAppDraftStatus? status, CancellationToken ct)
    {
        var items = await _repo.ListAsync<WhatsAppDraft>(d => status == null || d.Status == status, ct);
        return items.OrderByDescending(d => d.CreatedAt).Select(ToResponse).ToList();
    }

    /// <summary>The only place where a WhatsApp draft becomes a real entry.</summary>
    public async Task<EntryResponse> ConfirmDraftAsync(Guid id, ConfirmWhatsAppDraftRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var draft = await _repo.GetAsync<WhatsAppDraft>(id, ct) ?? throw new NotFoundException("Rascunho", id);
        if (draft.Status != WhatsAppDraftStatus.Pending)
            throw new ValidationException("status", "O rascunho já foi processado.");

        var payload = JsonSerializer.Deserialize<WhatsAppDraftPayload>(draft.PayloadJson, Json)
                      ?? throw new ValidationException("payload", "Rascunho inválido.");

        IReadOnlyList<EntryResponse> created = [];
        await _uow.ExecuteInTransactionAsync(async token =>
        {
            created = await _entries.CreateAsync(new CreateEntryRequest(
                payload.Type,
                payload.Amount,
                payload.OccurredAt,
                payload.Description,
                AccountId: request.AccountId,
                CategoryId: request.CategoryId), token);

            draft.Confirm();
            AuditRecorder.Record(_audits, _correlation, "WhatsAppDraft", draft.Id, "WhatsAppDraftConfirmed", new { payload.Type, payload.Amount });
            await _uow.SaveChangesAsync(token);
        }, ct);

        return created[0];
    }

    public async Task DiscardDraftAsync(Guid id, CancellationToken ct)
    {
        var draft = await _repo.GetAsync<WhatsAppDraft>(id, ct) ?? throw new NotFoundException("Rascunho", id);
        draft.Discard();
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>Handles a Cloud API webhook body, creates entries when needed, and sends WhatsApp replies.</summary>
    public async Task HandleWebhookAsync(string body, CancellationToken ct)
    {
        foreach (var message in ExtractInbound(body))
        {
            WhatsAppReply? reply = message.Kind switch
            {
                InboundKind.Text => await HandleMessageAsync(message.From, message.Text!, ct),
                InboundKind.Image => await HandleImageAsync(message.From, message.MediaId!, ct),
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

    public async Task<WhatsAppReply?> HandleImageAsync(string fromDigits, string mediaId, CancellationToken ct)
    {
        var phone = WhatsAppLink.NormalizePhone(fromDigits);
        var link = await _repo.FirstOrDefaultGlobalByKeyAsync<WhatsAppLink>(l => l.PhoneE164 == phone, ct);
        if (link is null)
            return new WhatsAppReply(phone, "Este número ainda não está vinculado. Abra o Conora e vincule seu telefone para usar o WhatsApp.");

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

            if (intake.Status == ReceiptIntakeStatus.Confirmed)
                return new WhatsAppReply(phone,
                    $"Lançamento gravado: R$ {(intake.ExtractedAmount ?? 0).ToString("N2", new CultureInfo("pt-BR"))} ({intake.ExtractedName ?? "recibo"}).");

            if (intake.NeedsClassification || intake.Status == ReceiptIntakeStatus.NeedsClassification)
                return new WhatsAppReply(phone, await BuildClassificationListAsync(intake.ExtractedName, ct));

            return new WhatsAppReply(phone, BuildConfirmPrompt(intake));
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
        var link = await _repo.FirstOrDefaultGlobalByKeyAsync<WhatsAppLink>(l => l.PhoneE164 == phone, ct);
        if (link is null)
            return new WhatsAppReply(phone, "Este número ainda não está vinculado. Abra o Conora e vincule seu telefone para usar o WhatsApp.");

        _tenant.Set(link.UsuarioId);

        var activeReceipt = await _receipts.FindActiveWhatsAppAsync(phone, ct);
        if (activeReceipt is not null)
        {
            var receiptReply = await HandleReceiptReplyAsync(activeReceipt, text.Trim(), ct);
            if (receiptReply is not null)
                return receiptReply;
        }

        if (IsQuery(text))
            return new WhatsAppReply(phone, await AnswerBudgetAsync(ct));

        try
        {
            await _plan.EnsureWritableAsync(ct);

            var payload = await ParseEntryAsync(text, ct);
            if (payload is null)
                return new WhatsAppReply(phone, "Não entendi o valor. Exemplo: \"gastei 45,90 no mercado\".");

            var draft = WhatsAppDraft.Create(phone, JsonSerializer.Serialize(payload, Json));
            _repo.Add(draft);
            AuditRecorder.Record(_audits, _correlation, "WhatsAppDraft", draft.Id, "WhatsAppDraftCreated",
                new { payload.Type, payload.Amount, Phone = phone });
            await _uow.SaveChangesAsync(ct);

            var kind = payload.Type == EntryType.Income ? "receita" : "despesa";
            var amount = payload.Amount.ToString("N2", new CultureInfo("pt-BR"));
            return new WhatsAppReply(phone,
                $"Entendi: {kind} de R$ {amount} ({payload.Description}). Abra o Conora e confirme o rascunho antes de gravar.");
        }
        catch (PlanReadOnlyException ex)
        {
            return new WhatsAppReply(phone, ex.Message);
        }
        catch (ValidationException ex)
        {
            var detail = ex.Errors.SelectMany(pair => pair.Value).FirstOrDefault() ?? ex.Message;
            return new WhatsAppReply(phone, $"Não consegui preparar o rascunho: {detail}");
        }
        catch (DomainException ex)
        {
            return new WhatsAppReply(phone, $"Não consegui preparar o rascunho: {ex.Message}");
        }
        catch (Exception)
        {
            return new WhatsAppReply(phone, "Não consegui preparar o rascunho. Tente de novo em instantes.");
        }
    }

    private async Task<WhatsAppReply?> HandleReceiptReplyAsync(ReceiptIntake intake, string text, CancellationToken ct)
    {
        var phone = intake.PhoneE164 ?? "";
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
                return new WhatsAppReply(phone, "Lançamento confirmado.");
            return new WhatsAppReply(phone, BuildConfirmPrompt(updated));
        }

        if (intake.WhatsAppAwaiting == WhatsAppReceiptAwaiting.Confirm)
        {
            if (text is "1" or "sim" or "Sim" or "SIM")
            {
                await _receipts.ConfirmAsync(intake.Id, new ConfirmReceiptRequest(), ct);
                return new WhatsAppReply(phone, "Lançamento confirmado.");
            }

            if (text is "2" or "alterar" or "Alterar")
            {
                await _receipts.ReopenClassificationAsync(intake.Id, ct);
                return new WhatsAppReply(phone, await BuildClassificationListAsync(intake.ExtractedName, ct));
            }

            return new WhatsAppReply(phone, "Responda 1 para confirmar ou 2 para alterar a conta.");
        }

        return null;
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

    private static string BuildConfirmPrompt(ReceiptIntakeResponse intake)
    {
        var br = new CultureInfo("pt-BR");
        var amount = intake.ExtractedAmount?.ToString("N2", br) ?? "?";
        var account = intake.ProposedCategoryName ?? intake.ProposedCategoryCode ?? "conta";
        var name = intake.ExtractedName ?? "fornecedor";
        return $"Confirma R$ {amount} em {account} ({name})?\n1 = Sim\n2 = Alterar conta";
    }

    private async Task<string> AnswerBudgetAsync(CancellationToken ct)
    {
        var ym = Competence.From(DateTime.UtcNow);
        var budget = await _budgets.GetAsync(ym, ct);
        var br = new CultureInfo("pt-BR");
        return $"Orçamento de {ym}: planejado R$ {budget.TotalPlanned.ToString("N2", br)}, gasto R$ {budget.TotalActual.ToString("N2", br)}.";
    }

    private async Task<WhatsAppDraftPayload?> ParseEntryAsync(string text, CancellationToken ct)
    {
        var fromAi = await TryParseWithGeminiAsync(text, ct);
        return fromAi ?? ParseWithRules(text);
    }

    private async Task<WhatsAppDraftPayload?> TryParseWithGeminiAsync(string text, CancellationToken ct)
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
            return new WhatsAppDraftPayload(
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

    private static WhatsAppDraftPayload? ParseWithRules(string text)
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
        return new WhatsAppDraftPayload(type, amount.Value, string.IsNullOrWhiteSpace(description) ? text.Trim() : description, DateTime.UtcNow);
    }

    private static bool IsQuery(string text)
    {
        var lowered = text.ToLowerInvariant();
        return QueryWords.Any(lowered.Contains) && !lowered.Any(char.IsDigit);
    }

    private static IEnumerable<InboundMessage> ExtractInbound(string body)
    {
        if (!TryParse(body, out var doc))
            yield break;

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
                yield break;

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value)
                        || !value.TryGetProperty("messages", out var messages)
                        || messages.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var message in messages.EnumerateArray())
                    {
                        var from = message.TryGetProperty("from", out var f) ? f.GetString() : null;
                        if (string.IsNullOrWhiteSpace(from))
                            continue;

                        var type = message.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                        if (type == "image"
                            && message.TryGetProperty("image", out var image)
                            && image.TryGetProperty("id", out var mediaIdEl))
                        {
                            var mediaId = mediaIdEl.GetString();
                            if (!string.IsNullOrWhiteSpace(mediaId))
                                yield return new InboundMessage(from, InboundKind.Image, null, mediaId);
                            continue;
                        }

                        string? text = null;
                        if (message.TryGetProperty("text", out var t) && t.TryGetProperty("body", out var b))
                            text = b.GetString();

                        if (!string.IsNullOrWhiteSpace(text))
                            yield return new InboundMessage(from, InboundKind.Text, text, null);
                    }
                }
            }
        }
    }

    private enum InboundKind { Text, Image }

    private sealed record InboundMessage(string From, InboundKind Kind, string? Text, string? MediaId);

    private static bool TryParse(string body, out JsonDocument doc)
    {
        try
        {
            doc = JsonDocument.Parse(body);
            return true;
        }
        catch (JsonException)
        {
            doc = null!;
            return false;
        }
    }

    private static WhatsAppDraftResponse ToResponse(WhatsAppDraft d)
        => new(d.Id, d.PhoneE164, d.PayloadJson, d.Status, d.CreatedAt);

    [GeneratedRegex(@"\d{1,3}(?:\.\d{3})+(?:,\d{1,2})?|\d+(?:[.,]\d{1,2})?")]
    private static partial Regex AmountRegex();
}
