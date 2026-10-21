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

    private const string SystemInstruction =
        "Você classifica um comando de voz do app Conora. Responda só JSON. " +
        "intent é navigate, action ou ask. " +
        "navigate: o usuário quer abrir ou mudar de tela, inclusive 'ir pra', 'ir para', 'mover pra tela' e 'vai pra'. route é um destes caminhos: " +
        "/ /orcamento /raio-x /lancamentos /categorias /contas /cartoes /projetos /patrimonio /membros /marcos /relatorios /diagnostico /importar /mes /perfil /configuracoes /ajuda /plano /whatsapp. " +
        "action create-budget: o usuário quer registrar um valor de orçamento numa categoria, não abrir a tela. entry.type fica null. entry.categoryName é a categoria. entry.amount é o valor previsto. " +
        "action create-entry: o usuário quer gravar um lançamento de despesa ou de receita. entry.type é Expense ou Income. " +
        "Receita, renda ou a receber é Income. Despesa, a pagar ou conta de é Expense. " +
        "entry.amount é número ou null. entry.date é hoje, ontem ou yyyy-MM-dd, ou null. " +
        "entry.description é o referente. entry.categoryName é a categoria. entry.accountName é a conta bancária. " +
        "Preencha todo campo que a frase trouxe, de uma vez. Não peça campo a campo. " +
        "Se a frase já tiver tipo, valor, data, descrição, categoria e conta, deixe todos preenchidos. " +
        "Preencha só o que foi dito. 'a pagar' é Expense. 'a receber' é Income. 'conta pessoal' é categoryName Pessoal. " +
        "ask: conversa. Você é o Ian. Fale pelo primeiro nome de quem está logado, de um jeito natural, como pessoa. " +
        "Se a pessoa cumprimentar, se apresentar ou perguntar quem você é e o que você pode fazer, responda no diálogo, sem frase pronta. " +
        "Se ela disser outro nome, reconheça, e continue tratando pelo primeiro nome da conta. " +
        "O que você faz de fato: abrir uma tela do Conora, lançar despesa ou receita, registrar orçamento e comentar a receita só com números que já foram informados. " +
        "Não invente valor, saldo, categoria nem dado que não veio. Se não souber, diga e pergunte. " +
        "reply de ask é a fala curta para áudio, com o primeiro nome. " +
        "Se for action, reply só resume o que foi entendido, sem pedir confirmação e sem perguntar campo que já veio na frase. " +
        "Nunca diga que já gravou ou que já abriu a tela. " +
        "Se a mensagem trouxer uma conversa anterior, continue essa mesma conversa. Use o que já foi dito e não recomece do zero. " +
        "Formato: {\"intent\":\"navigate|action|ask\",\"route\":null,\"action\":\"create-entry|create-budget|null\",\"entry\":null,\"reply\":\"...\"}.";

    private readonly IGeminiClient _gemini;
    private readonly IFinanceRepository _repo;
    private readonly IUserRepository _users;
    private readonly ITenantContext _tenant;
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ICorrelationContext _correlation;
    private readonly BudgetService _budget;

    public VoiceCommandService(
        IGeminiClient gemini,
        IFinanceRepository repo,
        IUserRepository users,
        ITenantContext tenant,
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        BudgetService budget)
    {
        _gemini = gemini;
        _repo = repo;
        _users = users;
        _tenant = tenant;
        _audits = audits;
        _uow = uow;
        _correlation = correlation;
        _budget = budget;
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
        CategorySection.Essential,
        CategorySection.Social,
        CategorySection.Asset,
        CategorySection.Liability,
    ];

    public async Task<SuggestCategoriesResponse> SuggestCategoriesAsync(SuggestCategoriesRequest request, CancellationToken ct)
    {
        var term = request.Term?.Trim() ?? string.Empty;
        if (term.Length == 0)
            return new SuggestCategoriesResponse([]);

        var income = string.Equals(request.Type, "Income", StringComparison.OrdinalIgnoreCase);
        var budget = string.Equals(request.Type, "Budget", StringComparison.OrdinalIgnoreCase);
        var contribution = string.Equals(request.Type, "ProjectContribution", StringComparison.OrdinalIgnoreCase);
        var sections = contribution
            ? new[] { CategorySection.LifeProject }
            : budget
            ? ExpenseSections.Append(CategorySection.Income).ToArray()
            : income
                ? [CategorySection.Income, CategorySection.LifeProject, CategorySection.Asset, CategorySection.Liability]
                : ExpenseSections;
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
        var speaker = await SpeakerAsync(ct);
        if (!_gemini.IsConfigured)
            return await WithSpokenAnswerAsync(local, transcript, request.CompetenceYm, speaker, ct);
        if (await OverHourlyLimitAsync(ct))
            return ToResponse(local with { Reply = Address(speaker, GeminiQuota.HourlyLimitMessage) });

        var raw = await _gemini.GenerateJsonAsync(SystemInstruction, PromptFor(request, transcript, speaker), ct);
        AuditRecorder.Record(_audits, _correlation, "Gemini", Guid.NewGuid(), "GeminiUsed", new { Length = transcript.Length, Kind = "voice", Ok = raw is not null });
        await _uow.SaveChangesAsync(ct);

        var parsed = raw is null ? null : TryParse(raw);
        var chosen = parsed ?? local;
        if (local.Intent == VoiceCommandClassifier.Navigate && chosen.Intent != VoiceCommandClassifier.Navigate)
            chosen = local;
        if (IanInsight.LooksLike(transcript) && local.Intent is not (VoiceCommandClassifier.Navigate or VoiceCommandClassifier.Action))
            chosen = new VoiceCommandClassifier.Result(VoiceCommandClassifier.Ask, null, null, null, "");
        else if (VoiceCommandClassifier.LooksLikeReport(transcript))
            chosen = new VoiceCommandClassifier.Result(
                VoiceCommandClassifier.Ask, null, null, null, VoiceCommandClassifier.ScopeReply);
        chosen = MergeEntry(chosen, local);
        return await WithSpokenAnswerAsync(chosen, transcript, request.CompetenceYm, speaker, ct);
    }

    public async Task<VoiceUserResponse> CurrentUserAsync(CancellationToken ct)
    {
        var speaker = await SpeakerAsync(ct);
        return new VoiceUserResponse(speaker?.Full ?? string.Empty, speaker?.First ?? string.Empty);
    }

    private async Task<(string Full, string First)?> SpeakerAsync(CancellationToken ct)
    {
        var id = _tenant.UsuarioId;
        if (id is null)
            return null;
        var user = await _users.GetByIdAsync(id.Value, ct);
        var full = user?.Name?.Trim();
        if (string.IsNullOrEmpty(full))
            return null;
        var first = full.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return (full, first);
    }

    private static string Address((string Full, string First)? speaker, string reply)
    {
        if (speaker is null || string.IsNullOrWhiteSpace(reply))
            return reply;
        if (reply.Contains(speaker.Value.First, StringComparison.OrdinalIgnoreCase))
            return reply;
        return $"{speaker.Value.First}, {reply}";
    }

    private async Task<bool> OverHourlyLimitAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        var used = await _repo.CountAsync<AuditEvent>(e => e.EntityName == "Gemini" && e.TimestampUtc > cutoff, ct);
        return used >= GeminiQuota.MaxCallsPerHour;
    }

    private static string PromptFor(VoiceCommandRequest request, string transcript, (string Full, string First)? speaker)
    {
        var lines = new System.Text.StringBuilder();
        if (speaker is not null)
            lines.Append("Pessoa logada: ").Append(speaker.Value.Full).Append(". Trate por ").Append(speaker.Value.First).AppendLine(".");

        var history = request.History;
        if (history is null || history.Count == 0)
        {
            if (lines.Length == 0)
                return transcript;
            lines.Append("Nova fala: ").Append(transcript);
            return lines.ToString();
        }

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
            if (intent is not (VoiceCommandClassifier.Navigate or VoiceCommandClassifier.Action or VoiceCommandClassifier.Ask or "report"))
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
                if (action is not (VoiceCommandClassifier.CreateEntry or VoiceCommandClassifier.CreateBudget)
                    || !root.TryGetProperty("entry", out var entry)
                    || entry.ValueKind != JsonValueKind.Object)
                    return null;
                var type = ReadString(entry, "type");
                if (action == VoiceCommandClassifier.CreateBudget)
                    type = "";
                else if (type == "ProjectContribution")
                    type = "Income";
                else if (type is not ("Expense" or "Income"))
                    type = "Expense";
                return new VoiceCommandClassifier.Result(
                    VoiceCommandClassifier.Action,
                    null,
                    action,
                    new VoiceCommandClassifier.Entry(
                        type,
                        ReadAmount(entry),
                        ReadString(entry, "description"),
                        ReadString(entry, "categoryName"),
                        ReadString(entry, "accountName"),
                        ReadString(entry, "date")),
                    ReadString(root, "reply") ?? "Entendi o lançamento.");
            }

            if (intent == "report")
                return new VoiceCommandClassifier.Result(
                    VoiceCommandClassifier.Ask,
                    null,
                    null,
                    null,
                    VoiceCommandClassifier.ScopeReply);

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
        (string Full, string First)? speaker,
        CancellationToken ct)
    {
        if (result.Intent != VoiceCommandClassifier.Ask)
            return ToResponse(result with { Reply = Address(speaker, result.Reply) });
        if (VoiceCommandClassifier.LooksLikeOpen(transcript))
        {
            if (IsModelReply(result.Reply))
                return ToResponse(result with { Reply = Address(speaker, result.Reply) });

            var line = await ComposeGreetingAsync(transcript, speaker, ct);
            if (!string.IsNullOrWhiteSpace(line))
                return ToResponse(result with { Reply = line });

            return ToResponse(result with { Reply = Address(speaker, VoiceCommandClassifier.OpenFallback(BrazilNow())) });
        }
        if (IanInsight.LooksLike(transcript))
        {
            var insight = await ComposeInsightAsync(transcript, competenceYm, ct);
            return ToResponse(result with { Reply = Address(speaker, insight) });
        }
        if (!string.IsNullOrWhiteSpace(result.Reply))
            return ToResponse(result with { Reply = Address(speaker, result.Reply) });

        var fallback = VoiceCommandClassifier.LooksLikeHelp(transcript)
            ? VoiceCommandClassifier.HelpReply
            : VoiceCommandClassifier.ScopeReply;
        return ToResponse(result with { Reply = Address(speaker, fallback) });
    }

    private static bool IsModelReply(string? reply)
    {
        var text = reply?.Trim() ?? "";
        return text.Length > 0
            && text != VoiceCommandClassifier.ScopeReply
            && text != VoiceCommandClassifier.HelpReply;
    }

    private static DateTime BrazilNow()
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        }
        catch (TimeZoneNotFoundException)
        {
            return DateTime.Now;
        }
    }

    private async Task<string?> ComposeGreetingAsync(
        string transcript,
        (string Full, string First)? speaker,
        CancellationToken ct)
    {
        if (!_gemini.IsConfigured)
            return null;
        if (await OverHourlyLimitAsync(ct))
            return Address(speaker, GeminiQuota.HourlyLimitMessage);

        var who = speaker is null ? "" : $"Trate por {speaker.Value.First}. ";
        var raw = await _gemini.GenerateJsonAsync(
            "Você é o Ian. Responda só JSON {\"reply\":\"...\"}. " +
            "A pessoa cumprimentou. Escreva uma frase falada, natural e curta. " +
            who +
            "Não liste funções do app e não use menu.",
            transcript,
            ct);
        AuditRecorder.Record(_audits, _correlation, "Gemini", Guid.NewGuid(), "GeminiUsed", new { Length = transcript.Length, Kind = "voice-greeting", Ok = raw is not null });
        await _uow.SaveChangesAsync(ct);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var reply = ReadString(doc.RootElement, "reply");
            return string.IsNullOrWhiteSpace(reply) ? null : Address(speaker, reply);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string> ComposeInsightAsync(string transcript, string? competenceYm, CancellationToken ct)
    {
        var end = Competence.IsValid(competenceYm) ? competenceYm! : Competence.From(DateTime.UtcNow);
        var count = IanInsight.MonthCount(transcript);
        var slices = new List<IanInsight.MonthSlice>(count);
        var accounts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var focus = IanInsight.Focus(transcript);

        for (var i = count - 1; i >= 0; i--)
        {
            var budget = await _budget.GetAsync(Competence.AddMonths(end, -i), ct);
            slices.Add(new IanInsight.MonthSlice(
                budget.ReceivedIncome,
                Actual(budget, CategorySection.Discount),
                Actual(budget, CategorySection.LifeProject),
                Actual(budget, CategorySection.Essential),
                Actual(budget, CategorySection.Social)));

            if (focus is not CategorySection section)
                continue;
            var block = budget.Sections.FirstOrDefault(item => item.Section == section);
            if (block is null)
                continue;
            foreach (var line in block.Lines)
            {
                if (line.IsGroup || line.CategoryId is null || line.ActualAmount == 0)
                    continue;
                accounts[line.CategoryName] = accounts.GetValueOrDefault(line.CategoryName) + line.ActualAmount;
            }
        }

        if (focus is CategorySection focused)
        {
            var rows = accounts.Select(item => new IanInsight.AccountSlice(item.Key, item.Value)).ToList();
            return IanInsight.DescribeSection(focused, rows, count);
        }

        return IanInsight.DescribeLeak(slices);
    }

    private static decimal Actual(BudgetResponse budget, CategorySection section)
        => budget.Sections.FirstOrDefault(item => item.Section == section)?.ActualAmount ?? 0;

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
