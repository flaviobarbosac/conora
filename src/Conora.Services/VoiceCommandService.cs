using System.Globalization;
using System.Text.Json;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

/// <summary>Turns a transcript into a command the app can run. Does not navigate or persist entries.</summary>
public sealed class VoiceCommandService
{
    private const int MaxLength = 1000;
    private const int MaxQuestionsPerHour = 20;

    private const string SystemInstruction =
        "Você classifica um comando de voz do app Conora. Responda só JSON. " +
        "intent é navigate, action, report ou ask. " +
        "navigate: o usuário quer abrir ou mudar de tela, inclusive 'ir pra', 'ir para', 'mover pra tela' e 'vai pra'. route é um destes caminhos: " +
        "/ /orcamento /raio-x /lancamentos /categorias /contas /cartoes /projetos /patrimonio /membros /marcos /relatorios /diagnostico /importar /mes /perfil /configuracoes /ajuda /plano /whatsapp. " +
        "action: o usuário quer gravar um lançamento. action = create-entry. entry.type é Expense ou Income. " +
        "entry.amount é número ou null. entry.date é hoje, ontem ou yyyy-MM-dd, ou null. " +
        "entry.description é o referente. entry.categoryName é a categoria. entry.accountName é a conta bancária. " +
        "Preencha todo campo que a frase trouxe, de uma vez. Não peça campo a campo. " +
        "Se a frase já tiver tipo, valor, data, descrição, categoria e conta, deixe todos preenchidos. " +
        "Preencha só o que foi dito. 'a pagar' é Expense. 'conta pessoal' é categoryName Pessoal. " +
        "report: o usuário pediu um relatório financeiro (mês, evolução, composição, onde gastou). route e action ficam null. " +
        "ask: qualquer outra pergunta, inclusive hora, data e conversa. Não transforme pergunta geral em relatório. route e action ficam null. " +
        "reply é uma frase curta em português. Em ask, reply é a resposta direta, sem falar de relatório de despesa. " +
        "Se for action, reply só resume o que foi entendido, sem pedir confirmação e sem perguntar campo que já veio na frase. " +
        "Nunca diga que já gravou ou que já abriu a tela. " +
        "Se a mensagem trouxer uma conversa anterior, continue essa mesma conversa. Use o que já foi dito e não recomece do zero. " +
        "Formato: {\"intent\":\"navigate|action|ask\",\"route\":null,\"action\":null,\"entry\":null,\"reply\":\"...\"}.";

    private readonly IGeminiClient _gemini;
    private readonly IFinanceRepository _repo;
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ICorrelationContext _correlation;
    private readonly IanReportService _ian;

    public VoiceCommandService(
        IGeminiClient gemini,
        IFinanceRepository repo,
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        IanReportService ian)
    {
        _gemini = gemini;
        _repo = repo;
        _audits = audits;
        _uow = uow;
        _correlation = correlation;
        _ian = ian;
    }

    public Task<byte[]?> SpeakAsync(string text, string? voice, CancellationToken ct)
    {
        var name = string.Equals(voice, "clara", StringComparison.OrdinalIgnoreCase) ? "Aoede" : "Charon";
        return _gemini.SpeakAsync(text, name, ct);
    }

    public const int MaxCategorySuggestions = 4;

    private static readonly CategorySection[] ExpenseSections =
    [
        CategorySection.Discount,
        CategorySection.LifeProject,
        CategorySection.Essential,
        CategorySection.Social,
    ];

    public async Task<SuggestCategoriesResponse> SuggestCategoriesAsync(SuggestCategoriesRequest request, CancellationToken ct)
    {
        var term = request.Term?.Trim() ?? string.Empty;
        if (term.Length == 0)
            return new SuggestCategoriesResponse([]);

        var income = string.Equals(request.Type, "Income", StringComparison.OrdinalIgnoreCase);
        var sections = income ? [CategorySection.Income] : ExpenseSections;
        var categories = await _repo.ListAsync<Category>(
            c => c.Level == CategoryLevel.Analytical && c.IsActive && !c.IsPendingReview && sections.Contains(c.Section),
            ct);
        var names = categories
            .Select(c => c.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
            return new SuggestCategoriesResponse([]);

        var ranked = names
            .Select(name => (name, score: CategoryScore(term, name)))
            .Where(item => item.score > 0 && item.score < 1000)
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.name)
            .Take(MaxCategorySuggestions)
            .ToList();

        if (ranked.Count < MaxCategorySuggestions && _gemini.IsConfigured && !await OverHourlyLimitAsync(ct))
        {
            foreach (var extra in await SemanticNamesAsync(term, names, ct))
            {
                if (ranked.Count >= MaxCategorySuggestions)
                    break;
                if (ranked.Any(name => string.Equals(name, extra, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (string.Equals(Fold(extra), Fold(term), StringComparison.Ordinal))
                    continue;
                ranked.Add(extra);
            }
        }

        return new SuggestCategoriesResponse(ranked);
    }

    private async Task<IReadOnlyList<string>> SemanticNamesAsync(string term, List<string> names, CancellationToken ct)
    {
        var list = string.Join('\n', names.Select(name => "- " + name));
        var raw = await _gemini.GenerateJsonAsync(
            "Você escolhe categorias já cadastradas. Responda só JSON {\"names\":[]}. " +
            "No máximo 4 nomes, copiados exatamente da lista. Não invente. " +
            "Escolha os mais próximos do termo falado pelo sentido.",
            "Termo: " + term + "\nLista:\n" + list,
            ct);
        AuditRecorder.Record(_audits, _correlation, "Gemini", Guid.NewGuid(), "GeminiUsed", new { Length = term.Length, Kind = "voice-categories", Ok = raw is not null });
        await _uow.SaveChangesAsync(ct);
        if (string.IsNullOrWhiteSpace(raw))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("names", out var node))
                return [];
            var allowed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                var key = Fold(name);
                if (!allowed.ContainsKey(key))
                    allowed[key] = name;
            }
            var picked = new List<string>();
            foreach (var item in node.EnumerateArray())
            {
                if (picked.Count >= MaxCategorySuggestions)
                    break;
                var text = item.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                if (allowed.TryGetValue(Fold(text), out var canonical) && !picked.Contains(canonical, StringComparer.OrdinalIgnoreCase))
                    picked.Add(canonical);
            }
            return picked;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int CategoryScore(string spoken, string name)
    {
        var needle = Fold(spoken);
        var hay = Fold(name);
        if (needle.Length == 0 || hay.Length == 0)
            return 0;
        if (needle == hay)
            return 1000;
        if (hay.Contains(needle, StringComparison.Ordinal) || needle.Contains(hay, StringComparison.Ordinal))
            return 800 - Math.Abs(needle.Length - hay.Length);
        var prefix = 0;
        var limit = Math.Min(needle.Length, hay.Length);
        while (prefix < limit && needle[prefix] == hay[prefix])
            prefix++;
        if (prefix >= 4)
            return 500 + prefix;
        var distance = EditDistance(needle, hay);
        var ratio = 1d - (distance / (double)Math.Max(needle.Length, hay.Length));
        return ratio >= 0.55 ? (int)(ratio * 400) : 0;
    }

    private static int EditDistance(string left, string right)
    {
        var rows = left.Length + 1;
        var cols = right.Length + 1;
        var grid = new int[rows, cols];
        for (var i = 0; i < rows; i++)
            grid[i, 0] = i;
        for (var j = 0; j < cols; j++)
            grid[0, j] = j;
        for (var i = 1; i < rows; i++)
        {
            for (var j = 1; j < cols; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                grid[i, j] = Math.Min(Math.Min(grid[i - 1, j] + 1, grid[i, j - 1] + 1), grid[i - 1, j - 1] + cost);
            }
        }
        return grid[left.Length, right.Length];
    }

    private static string Fold(string value)
    {
        var chars = value.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray();
        return new string(chars).ToLowerInvariant().Trim();
    }

    public async Task<VoiceCommandResponse> ClassifyAsync(VoiceCommandRequest request, CancellationToken ct)
    {
        var transcript = request.Transcript?.Trim();
        if (string.IsNullOrWhiteSpace(transcript))
            throw new ValidationException("transcript", "Não ouvi nada.");
        if (transcript.Length > MaxLength)
            throw new ValidationException("transcript", $"O comando deve ter no máximo {MaxLength} caracteres.");

        var local = VoiceCommandClassifier.Classify(transcript);
        if (!_gemini.IsConfigured || await OverHourlyLimitAsync(ct))
            return await WithSpokenAnswerAsync(local, transcript, request.CompetenceYm, ct);

        var raw = await _gemini.GenerateJsonAsync(SystemInstruction, PromptFor(request, transcript), ct);
        AuditRecorder.Record(_audits, _correlation, "Gemini", Guid.NewGuid(), "GeminiUsed", new { Length = transcript.Length, Kind = "voice", Ok = raw is not null });
        await _uow.SaveChangesAsync(ct);

        var parsed = raw is null ? null : TryParse(raw);
        var chosen = parsed ?? local;
        if (local.Intent == VoiceCommandClassifier.Navigate && chosen.Intent != VoiceCommandClassifier.Navigate)
            chosen = local;
        if (chosen.Intent == VoiceCommandClassifier.Report && !VoiceCommandClassifier.LooksLikeReport(transcript))
            chosen = local.Intent == VoiceCommandClassifier.Report ? local : chosen with { Intent = VoiceCommandClassifier.Ask };
        if (local.Intent == VoiceCommandClassifier.Report && chosen.Intent == VoiceCommandClassifier.Ask)
            chosen = local;
        chosen = MergeEntry(chosen, local);
        return await WithSpokenAnswerAsync(chosen, transcript, request.CompetenceYm, ct);
    }

    private async Task<bool> OverHourlyLimitAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        var used = await _repo.CountAsync<AuditEvent>(e => e.EntityName == "Gemini" && e.TimestampUtc > cutoff, ct);
        return used >= MaxQuestionsPerHour;
    }

    private static string PromptFor(VoiceCommandRequest request, string transcript)
    {
        var history = request.History;
        if (history is null || history.Count == 0)
            return transcript;

        var lines = new System.Text.StringBuilder();
        lines.AppendLine("Conversa anterior. Continue a mesma conversa e use o que já foi dito:");
        foreach (var turn in history.TakeLast(12))
        {
            var text = turn.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;
            if (text.Length > 400)
                text = text[..400];
            var who = string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "Assistente" : "Usuário";
            lines.Append(who).Append(": ").AppendLine(text);
        }

        lines.Append("Nova fala: ").Append(transcript);
        return lines.ToString();
    }

    private static VoiceCommandClassifier.Result MergeEntry(
        VoiceCommandClassifier.Result chosen,
        VoiceCommandClassifier.Result local)
    {
        if (chosen.Entry is null || local.Entry is null)
            return chosen;
        if (chosen.Intent != VoiceCommandClassifier.Action || local.Intent != VoiceCommandClassifier.Action)
            return chosen;
        var primary = chosen.Entry;
        var extra = local.Entry;
        return chosen with
        {
            Entry = primary with
            {
                Type = string.IsNullOrWhiteSpace(primary.Type) ? extra.Type : primary.Type,
                Amount = primary.Amount ?? extra.Amount,
                Description = Blank(primary.Description) ? extra.Description : primary.Description,
                CategoryName = Blank(primary.CategoryName) ? extra.CategoryName : primary.CategoryName,
                AccountName = Blank(primary.AccountName) ? extra.AccountName : primary.AccountName,
                Date = Blank(primary.Date) ? extra.Date : primary.Date,
            },
        };
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    private static VoiceCommandClassifier.Result? TryParse(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var intent = ReadString(root, "intent");
            if (intent is not (VoiceCommandClassifier.Navigate or VoiceCommandClassifier.Action or VoiceCommandClassifier.Ask or VoiceCommandClassifier.Report))
                return null;

            var route = ReadString(root, "route");
            if (intent == VoiceCommandClassifier.Navigate)
            {
                if (!VoiceCommandClassifier.IsAllowedRoute(route))
                    return null;
                return new VoiceCommandClassifier.Result(
                    VoiceCommandClassifier.Navigate,
                    route,
                    null,
                    null,
                    ReadString(root, "reply") ?? "Abrindo a tela.");
            }

            if (intent == VoiceCommandClassifier.Action)
            {
                var action = ReadString(root, "action");
                if (action != VoiceCommandClassifier.CreateEntry || !root.TryGetProperty("entry", out var entry) || entry.ValueKind != JsonValueKind.Object)
                    return null;
                var type = ReadString(entry, "type");
                if (type is not ("Expense" or "Income"))
                    type = "Expense";
                return new VoiceCommandClassifier.Result(
                    VoiceCommandClassifier.Action,
                    null,
                    VoiceCommandClassifier.CreateEntry,
                    new VoiceCommandClassifier.Entry(
                        type,
                        ReadAmount(entry),
                        ReadString(entry, "description"),
                        ReadString(entry, "categoryName"),
                        ReadString(entry, "accountName"),
                        ReadString(entry, "date")),
                    ReadString(root, "reply") ?? "Entendi o lançamento.");
            }

            if (intent == VoiceCommandClassifier.Report)
                return new VoiceCommandClassifier.Result(
                    VoiceCommandClassifier.Report,
                    null,
                    null,
                    null,
                    ReadString(root, "reply") ?? "Vou montar o relatório.");

            return new VoiceCommandClassifier.Result(VoiceCommandClassifier.Ask, null, null, null, ReadString(root, "reply") ?? string.Empty);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal? ReadAmount(JsonElement entry)
    {
        if (!entry.TryGetProperty("amount", out var amount) || amount.ValueKind is JsonValueKind.Null)
            return null;
        if (amount.ValueKind == JsonValueKind.Number && amount.TryGetDecimal(out var value) && value > 0)
            return decimal.Round(value, 2);
        if (amount.ValueKind == JsonValueKind.String
            && decimal.TryParse(amount.GetString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0)
            return decimal.Round(parsed, 2);
        return null;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private async Task<VoiceCommandResponse> WithSpokenAnswerAsync(
        VoiceCommandClassifier.Result result,
        string transcript,
        string? competenceYm,
        CancellationToken ct)
    {
        if (result.Intent == VoiceCommandClassifier.Ask)
            return ToResponse(result with { Reply = GeneralReply(transcript, result.Reply) });

        if (result.Intent != VoiceCommandClassifier.Report)
            return ToResponse(result);

        var answer = await _ian.AskAsync(new IanAskRequest(transcript, competenceYm), ct);
        var spoken = answer.Answer?.Trim() ?? string.Empty;
        if (answer.Choices is { Count: > 0 })
            spoken = string.IsNullOrEmpty(spoken)
                ? string.Join(". ", answer.Choices)
                : $"{spoken} {string.Join(". ", answer.Choices)}.";

        return new VoiceCommandResponse(
            VoiceCommandClassifier.Report,
            null,
            null,
            null,
            string.IsNullOrWhiteSpace(spoken) ? "Não consegui montar o relatório." : spoken,
            answer.CanOpenReport ? answer.Query : null);
    }

    private static string GeneralReply(string transcript, string? modelReply)
    {
        var clock = ClockReply(transcript);
        if (clock is not null)
            return clock;
        if (GeminiService.LooksLikeTradeAdvice(transcript))
            return GeminiService.TradeAdviceRefusal;

        var reply = modelReply?.Trim() ?? string.Empty;
        var folded = IanSubjectMatcher.Normalize(reply);
        var talksAboutReport = folded.Contains("relatorio");
        if (reply.Length > 0 && !IsExpenseReportFallback(reply) && !(talksAboutReport && !VoiceCommandClassifier.LooksLikeReport(transcript)))
            return reply;
        return "Não tenho um relatório para isso. Pode perguntar de outro jeito, pedir uma tela ou um lançamento.";
    }

    private static bool IsExpenseReportFallback(string reply)
    {
        var text = IanSubjectMatcher.Normalize(reply);
        return text.Contains("relatorio") && (text.Contains("despesa") || text.Contains("renda"));
    }

    private static string? ClockReply(string transcript)
    {
        var text = IanSubjectMatcher.Normalize(transcript);
        var now = BrazilNow();
        if (text.Contains("que horas") || text.Contains("qual e a hora") || text.Contains("hora agora") || text.Contains("me diz a hora"))
            return $"Agora são {now:HH} horas e {now:mm} minutos.";
        if (text.Contains("que dia") || text.Contains("data de hoje") || text.Contains("qual e a data"))
            return $"Hoje é {now.ToString("dddd, d 'de' MMMM 'de' yyyy", CultureInfo.GetCultureInfo("pt-BR"))}.";
        return null;
    }

    private static DateTime BrazilNow()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(id));
            }
            catch (TimeZoneNotFoundException)
            {
                /* try the next id */
            }
            catch (InvalidTimeZoneException)
            {
                /* try the next id */
            }
        }

        return DateTime.UtcNow.AddHours(-3);
    }

    private static VoiceCommandResponse ToResponse(VoiceCommandClassifier.Result result)
        => new(
            result.Intent,
            result.Route,
            result.Action,
            result.Entry is null
                ? null
                : new VoiceEntryDraft(
                    result.Entry.Type,
                    result.Entry.Amount,
                    result.Entry.Description,
                    result.Entry.CategoryName,
                    result.Entry.AccountName,
                    result.Entry.Date),
            result.Reply);
}
