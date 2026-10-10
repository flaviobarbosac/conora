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

/// <summary>
/// Ian: classifies a natural-language data question, loads only the logged-in user's numbers,
/// and returns a static report shape (metrics + chart kind). Gemini never invents amounts.
/// </summary>
public sealed class IanReportService
{
    private const int MaxQuestionLength = 1000;
    private const int MaxQuestionsPerHour = 20;

    private const int MaxAnalyzeCategories = 12;
    private const int MaxInsightLength = 240;

    private const string IntentInstruction =
        "Você classifica perguntas do assistente Ian do Conora. " +
        "Responda estritamente em JSON: " +
        "{\"kind\":\"snapshot|series|composition\",\"focus\":\"expense|income|null\",\"competenceYm\":\"yyyy-MM ou null\",\"fromYm\":\"yyyy-MM ou null\",\"toYm\":\"yyyy-MM ou null\",\"subject\":\"texto livre ou null\"}. " +
        "snapshot = resumo do mês inteiro (receita x despesa x resultado) — só quando perguntar o mês como um todo, sem nomear conta/categoria/lançamento. " +
        "composition = partes de um total no mês com gráfico de pizza. Use composition + focus expense para despesas/gastos do mês sem nomear um item. " +
        "composition + focus income para receitas do mês sem nomear um item. " +
        "series = obrigatório quando a pergunta nomeia conta, categoria ou lançamento (ex.: \"quanto recebi na conta Recebimento de Salario este ano\", \"evolução da EDP\"). " +
        "Nesse caso preencha subject com o nome (sem aspas) e fromYm/toYm com o ano pedido (este/neste/deste ano → janeiro até o mês atual). " +
        "Nunca use snapshot se houver subject nomeado. " +
        "Se a pergunta não for sobre dados financeiros do usuário, use kind null: {\"kind\":null}. " +
        "Nunca invente valores monetários.";

    private const string AnalyzePresentationInstruction =
        "Você é o Ian do Conora. Recebe um CONTEXTO JSON com números já calculados pelo servidor. " +
        "Nunca invente, altere ou some valores. " +
        "Responda estritamente em JSON: " +
        "{\"chartKind\":\"line|pie|bar|none\",\"title\":\"título curto\",\"insight\":\"uma frase em pt-BR sobre o que os números mostram\"}. " +
        "line = evolução no tempo (1 conta, vários meses). " +
        "pie = composição/comparação de várias contas no período. " +
        "bar = poucos pontos sem série temporal clara. " +
        "none = só tabela (poucos dados). " +
        "insight deve citar apenas valores presentes no contexto.";

    private readonly IGeminiClient _gemini;
    private readonly IFinanceRepository _repo;
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ICorrelationContext _correlation;
    private readonly DashboardService _dashboard;
    private readonly CategoryService _categories;

    public IanReportService(
        IGeminiClient gemini,
        IFinanceRepository repo,
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        DashboardService dashboard,
        CategoryService categories)
    {
        _gemini = gemini;
        _repo = repo;
        _audits = audits;
        _uow = uow;
        _correlation = correlation;
        _dashboard = dashboard;
        _categories = categories;
    }

    public async Task<IanAskResponse> AskAsync(IanAskRequest request, CancellationToken ct)
    {
        var question = request.Question?.Trim();
        if (string.IsNullOrWhiteSpace(question))
            throw new ValidationException("question", "Escreva sua pergunta.");
        if (question.Length > MaxQuestionLength)
            throw new ValidationException("question", $"A pergunta deve ter no máximo {MaxQuestionLength} caracteres.");

        if (GeminiService.LooksLikeTradeAdvice(question))
            return new IanAskResponse(true, GeminiService.TradeAdviceRefusal, false);

        if (await IsRateLimitedAsync(ct))
            return new IanAskResponse(true, "Você atingiu o limite de perguntas por hora. Tente novamente mais tarde.", false);

        var fallbackYm = string.IsNullOrWhiteSpace(request.CompetenceYm) || !Competence.IsValid(request.CompetenceYm)
            ? Competence.From(DateTime.UtcNow)
            : request.CompetenceYm!;

        // Without Gemini, classify with local heuristics so reports still open.
        // RefineIntent corrects common misses (e.g. "despesas este mês" must be pie, not snapshot).
        var intent = RefineIntent(
            question,
            _gemini.IsConfigured
                ? await ClassifyAsync(question, fallbackYm, ct)
                : FromHeuristic(IanQuestionClassifier.Classify(question, fallbackYm)),
            fallbackYm);
        await RecordUsageAsync(question.Length, intent is not null, ct);

        if (intent is null || string.IsNullOrWhiteSpace(intent.Kind))
        {
            return new IanAskResponse(
                true,
                "Posso montar um relatório dos seus dados. Exemplos: “Como está meu mês?” ou “Evolução da EDP neste ano”.",
                false);
        }

        var kind = NormalizeKind(intent.Kind);
        if (kind is null)
        {
            return new IanAskResponse(
                true,
                "Não entendi o tipo de relatório. Peça o resumo do mês, a evolução de um gasto ou a composição do mês.",
                false);
        }

        var competenceYm = Competence.IsValid(intent.CompetenceYm) ? intent.CompetenceYm! : fallbackYm;
        var (fromYm, toYm) = ResolveRange(kind, intent.FromYm, intent.ToYm, competenceYm);
        var subject = string.IsNullOrWhiteSpace(intent.Subject) ? null : intent.Subject.Trim();

        if (kind is IanChartSelector.KindSeries && subject is null)
        {
            return new IanAskResponse(
                true,
                "Para evolução no tempo, diga o nome da categoria, da conta ou do lançamento. Ex.: “Evolução da EDP neste ano”.",
                false);
        }

        string? matchKind = null;
        Guid? matchId = null;
        string? matchLabel = null;

        if (subject is not null)
        {
            var matches = await FindSubjectMatchesAsync(subject, ct);
            if (matches.Count == 0)
            {
                return new IanAskResponse(
                    true,
                    $"Não encontrei “{subject}” nas suas categorias, lançamentos ou contas.",
                    false);
            }

            if (matches.Count > 1)
            {
                var labels = matches.Select(m => $"{m.Label} ({LabelForMatchKind(m.MatchKind)})").Distinct().Take(8).ToList();
                return new IanAskResponse(
                    true,
                    "Encontrei mais de um resultado. Escolha um e pergunte de novo com o nome completo.",
                    false,
                    Choices: labels);
            }

            matchKind = matches[0].MatchKind;
            matchId = matches[0].Id;
            matchLabel = matches[0].Label;
        }

        var focus = NormalizeFocus(intent.Focus);
        var query = new IanReportQuery(kind, competenceYm, fromYm, toYm, subject ?? matchLabel, matchKind, matchId, focus);
        var preview = await BuildReportAsync(query, ct);
        var answer = preview.Found
            ? $"Pronto. Montei o relatório “{preview.Title}” com os seus números."
            : $"Montei “{preview.Title}”, mas não há valores nesse período.";

        return new IanAskResponse(true, answer, true, query);
    }

    public async Task<IanAskResponse> AnalyzeAsync(IanAnalyzeRequest request, CancellationToken ct)
    {
        if (await IsRateLimitedAsync(ct))
            return new IanAskResponse(true, "Você atingiu o limite de perguntas por hora. Tente novamente mais tarde.", false);

        var ids = (request.CategoryIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(MaxAnalyzeCategories)
            .ToList();
        if (ids.Count == 0)
            throw new ValidationException("categoryIds", "Escolha ao menos uma conta do plano.");

        var fromYm = Competence.Require(request.FromYm);
        var toYm = Competence.Require(request.ToYm);
        if (Competence.Compare(fromYm, toYm) > 0)
            (fromYm, toYm) = (toYm, fromYm);

        var built = await BuildAnalyzeReportAsync(ids, fromYm, toYm, preferredChartKind: null, preferredTitle: null, preferredInsight: null, askAi: true, ct);
        await RecordUsageAsync(ids.Count * 8, built.Report.Found, ct);

        var query = new IanReportQuery(
            IanChartSelector.KindAnalyze,
            toYm,
            fromYm,
            toYm,
            built.Report.Title,
            null,
            null,
            null,
            string.Join(',', ids),
            built.Report.ChartKind,
            built.Report.Insight);

        var answer = built.Report.Found
            ? (built.Report.Insight ?? $"Pronto. Montei “{built.Report.Title}”.")
            : $"Montei “{built.Report.Title}”, mas não há valores nesse período.";

        return new IanAskResponse(true, answer, true, query);
    }

    public Task<IanReportResponse> GetReportAsync(IanReportQuery query, CancellationToken ct)
        => BuildReportAsync(query, ct);

    internal async Task<IanReportResponse> BuildReportAsync(IanReportQuery query, CancellationToken ct)
    {
        var kind = NormalizeKind(query.Kind)
            ?? throw new ValidationException("kind", "Tipo de relatório inválido.");

        return kind switch
        {
            IanChartSelector.KindSnapshot => await BuildSnapshotAsync(query, ct),
            IanChartSelector.KindComposition => await BuildCompositionAsync(query, ct),
            IanChartSelector.KindSeries => await BuildSeriesAsync(query, ct),
            IanChartSelector.KindAnalyze => await RebuildAnalyzeAsync(query, ct),
            _ => throw new ValidationException("kind", "Tipo de relatório inválido."),
        };
    }

    private async Task<IanReportResponse> RebuildAnalyzeAsync(IanReportQuery query, CancellationToken ct)
    {
        var ids = ParseCategoryIds(query.CategoryIds);
        if (ids.Count == 0)
            throw new ValidationException("categoryIds", "Contas da análise inválidas.");

        var fromYm = Competence.Require(query.FromYm ?? query.CompetenceYm);
        var toYm = Competence.Require(query.ToYm ?? query.CompetenceYm);
        var built = await BuildAnalyzeReportAsync(
            ids,
            fromYm,
            toYm,
            preferredChartKind: query.ChartKind,
            preferredTitle: query.Subject,
            preferredInsight: query.Insight,
            askAi: false,
            ct);
        return built.Report;
    }

    private async Task<IanReportResponse> BuildSnapshotAsync(IanReportQuery query, CancellationToken ct)
    {
        var ym = Competence.Require(query.CompetenceYm ?? Competence.From(DateTime.UtcNow));
        var dash = await _dashboard.GetAsync(ym, ct);
        var points = new List<IanReportPoint>
        {
            new("income", "Receita", dash.IncomeTotal),
            new("expense", "Despesa", dash.ExpenseTotal),
        };
        var found = dash.IncomeTotal != 0 || dash.ExpenseTotal != 0;
        return BuildLabeledAmountReport(
            $"Receita × despesa — {ym}",
            points,
            IanChartSelector.KindSnapshot,
            found,
            includeTotalRow: true,
            extraRows: [new IanTableRow("result", ["Resultado", FormatMoney(dash.Result)], null)]);
    }

    private async Task<IanReportResponse> BuildCompositionAsync(IanReportQuery query, CancellationToken ct)
    {
        var ym = Competence.Require(query.CompetenceYm ?? Competence.From(DateTime.UtcNow));
        var report = await _dashboard.GetReportAsync(ym, ct);
        var focus = NormalizeFocus(query.Focus) ?? IanQuestionClassifier.FocusExpense;
        var categories = (await _categories.ListAsync(null, true, true, ct)).ToDictionary(c => c.Id);
        var points = report.ByAccount
            .Where(c => c.Amount != 0 && c.CategoryId is Guid id && MatchesFocus(categories.GetValueOrDefault(id)?.Section, focus))
            .Take(12)
            .Select(c => new IanReportPoint(
                c.CategoryId?.ToString() ?? "none",
                c.CategoryName,
                c.Amount))
            .ToList();
        var label = focus == IanQuestionClassifier.FocusIncome ? "Receitas" : "Despesas";
        var found = points.Count > 0;
        return BuildLabeledAmountReport(
            $"{label} de {ym}",
            points,
            IanChartSelector.KindComposition,
            found,
            includeTotalRow: true);
    }

    private static bool MatchesFocus(CategorySection? section, string focus)
    {
        if (section is null)
            return false;
        return focus == IanQuestionClassifier.FocusIncome
            ? section is CategorySection.Income
            : section is CategorySection.Essential
                or CategorySection.Social
                or CategorySection.Expense
                or CategorySection.Discount
                or CategorySection.LifeProject;
    }

    private async Task<IanReportResponse> BuildSeriesAsync(IanReportQuery query, CancellationToken ct)
    {
        var fromYm = Competence.Require(query.FromYm ?? query.CompetenceYm);
        var toYm = Competence.Require(query.ToYm ?? query.CompetenceYm);
        if (Competence.Compare(fromYm, toYm) > 0)
            (fromYm, toYm) = (toYm, fromYm);

        var months = Competence.RangeInclusive(fromYm, toYm, "fromYm", "toYm");
        var entries = await LoadMatchedEntriesAsync(query, fromYm, toYm, ct);
        var byMonth = months.ToDictionary(m => m, _ => 0m);
        foreach (var e in entries)
        {
            if (byMonth.ContainsKey(e.CompetenceYm))
                byMonth[e.CompetenceYm] += e.Amount;
        }

        var points = months
            .Select(m => new IanReportPoint(m, m, byMonth[m]))
            .ToList();
        var label = query.Subject?.Trim() is { Length: > 0 } s ? s : "Série";
        var found = points.Any(p => p.Amount != 0);
        return BuildLabeledAmountReport(
            $"Evolução de {label} ({fromYm} a {toYm})",
            points,
            IanChartSelector.KindSeries,
            found,
            includeTotalRow: true,
            descriptionHeader: "Competência");
    }

    private static IanReportResponse BuildLabeledAmountReport(
        string title,
        IReadOnlyList<IanReportPoint> points,
        string kind,
        bool found,
        bool includeTotalRow,
        string descriptionHeader = "Descrição",
        IReadOnlyList<IanTableRow>? extraRows = null)
    {
        var columns = new[]
        {
            new IanTableColumn("description", descriptionHeader),
            new IanTableColumn("amount", "Valor"),
        };
        var rows = points
            .Select(p => new IanTableRow(p.Key, [p.Label, FormatMoney(p.Amount)], p.Amount))
            .ToList();
        if (includeTotalRow && points.Count > 0)
            rows.Add(new IanTableRow("total", ["Total", FormatMoney(points.Sum(p => p.Amount))], null));
        if (extraRows is not null)
            rows.AddRange(extraRows);

        // Series keeps zero months so the line shows the full timeline.
        // Pie/snapshot drop zeros — empty slices don't help.
        var chartPoints = kind == IanChartSelector.KindSeries
            ? points.ToList()
            : points.Where(p => p.Amount != 0).ToList();
        var chartKind = found
            ? IanChartSelector.Choose(kind, chartPoints.Count)
            : IanChartSelector.None;

        return new IanReportResponse(
            title,
            new IanReportTable(columns, rows),
            chartKind,
            chartPoints,
            found);
    }

    private sealed record AnalyzeBuildResult(IanReportResponse Report, int CategoryCount, int MonthCount);

    private async Task<AnalyzeBuildResult> BuildAnalyzeReportAsync(
        IReadOnlyList<Guid> categoryIds,
        string fromYm,
        string toYm,
        string? preferredChartKind,
        string? preferredTitle,
        string? preferredInsight,
        bool askAi,
        CancellationToken ct)
    {
        var months = Competence.RangeInclusive(fromYm, toYm, "fromYm", "toYm");
        var categories = (await _categories.ListAsync(null, true, true, ct))
            .Where(c => categoryIds.Contains(c.Id))
            .ToList();
        if (categories.Count == 0)
            throw new ValidationException("categoryIds", "Nenhuma conta válida encontrada.");

        var idSet = categories.Select(c => c.Id).ToHashSet();
        var entries = await _repo.ListAsync<Entry>(
            e => e.CompetenceYm.CompareTo(fromYm) >= 0
                 && e.CompetenceYm.CompareTo(toYm) <= 0
                 && e.CategoryId != null
                 && idSet.Contains(e.CategoryId.Value)
                 && (e.Type == EntryType.Expense
                     || e.Type == EntryType.Income
                     || e.Type == EntryType.Contribution),
            ct);

        IReadOnlyList<IanReportPoint> points;
        string descriptionHeader;
        string defaultTitle;
        if (categories.Count == 1)
        {
            var cat = categories[0];
            var byMonth = months.ToDictionary(m => m, _ => 0m);
            foreach (var e in entries)
            {
                if (byMonth.ContainsKey(e.CompetenceYm))
                    byMonth[e.CompetenceYm] += e.Amount;
            }

            points = months.Select(m => new IanReportPoint(m, m, byMonth[m])).ToList();
            descriptionHeader = "Competência";
            defaultTitle = $"Evolução de {cat.Name} ({fromYm} a {toYm})";
        }
        else
        {
            var byCat = categories.ToDictionary(c => c.Id, _ => 0m);
            foreach (var e in entries)
            {
                if (e.CategoryId is Guid cid && byCat.ContainsKey(cid))
                    byCat[cid] += e.Amount;
            }

            points = categories
                .Select(c => new IanReportPoint(c.Id.ToString(), c.Name, byCat[c.Id]))
                .Where(p => p.Amount != 0)
                .OrderByDescending(p => Math.Abs(p.Amount))
                .ToList();
            descriptionHeader = "Conta";
            defaultTitle = $"Contas selecionadas ({fromYm} a {toYm})";
        }

        var found = points.Any(p => p.Amount != 0);
        var chartPoints = categories.Count == 1
            ? points.ToList()
            : points.Where(p => p.Amount != 0).ToList();
        var fallbackChart = found
            ? IanChartSelector.ChooseAnalyze(categories.Count, months.Count, chartPoints.Count(p => p.Amount != 0 || categories.Count == 1))
            : IanChartSelector.None;

        // For single-account series, ChooseAnalyze needs >=2 chart points including zeros → use months.Count
        if (found && categories.Count == 1 && months.Count >= 2)
            fallbackChart = IanChartSelector.Line;
        else if (found && categories.Count >= 2 && chartPoints.Count >= 2)
            fallbackChart = IanChartSelector.Pie;
        else if (found && chartPoints.Count(p => p.Amount != 0) <= 1)
            fallbackChart = IanChartSelector.None;

        string chartKind = fallbackChart;
        string title = preferredTitle?.Trim() is { Length: > 0 } t ? t : defaultTitle;
        string? insight = string.IsNullOrWhiteSpace(preferredInsight) ? null : preferredInsight.Trim();

        if (askAi && _gemini.IsConfigured && found)
        {
            var presentation = await SuggestAnalyzePresentationAsync(
                categories.Select(c => c.Name).ToList(),
                fromYm,
                toYm,
                points,
                fallbackChart,
                ct);
            if (presentation is not null)
            {
                chartKind = NormalizeChartKind(presentation.ChartKind) ?? fallbackChart;
                if (!string.IsNullOrWhiteSpace(presentation.Title))
                    title = presentation.Title.Trim();
                if (!string.IsNullOrWhiteSpace(presentation.Insight))
                    insight = TruncateInsight(presentation.Insight.Trim());
            }
        }
        else if (!string.IsNullOrWhiteSpace(preferredChartKind))
        {
            chartKind = NormalizeChartKind(preferredChartKind) ?? fallbackChart;
        }

        // Guard: never trust AI with a chart that the data shape cannot support.
        if (chartKind == IanChartSelector.Line && (categories.Count != 1 || months.Count < 2))
            chartKind = fallbackChart;
        if (chartKind == IanChartSelector.Pie && chartPoints.Count(p => p.Amount != 0) < 2)
            chartKind = fallbackChart;

        insight ??= found
            ? BuildLocalInsight(categories.Count, points, fromYm, toYm)
            : null;

        var columns = new[]
        {
            new IanTableColumn("description", descriptionHeader),
            new IanTableColumn("amount", "Valor"),
        };
        var rows = points
            .Select(p => new IanTableRow(p.Key, [p.Label, FormatMoney(p.Amount)], p.Amount))
            .ToList();
        if (points.Count > 0)
            rows.Add(new IanTableRow("total", ["Total", FormatMoney(points.Sum(p => p.Amount))], null));

        var report = new IanReportResponse(
            title,
            new IanReportTable(columns, rows),
            found ? chartKind : IanChartSelector.None,
            chartKind == IanChartSelector.Line ? chartPoints : chartPoints.Where(p => p.Amount != 0).ToList(),
            found,
            insight);

        return new AnalyzeBuildResult(report, categories.Count, months.Count);
    }

    private async Task<AnalyzePresentation?> SuggestAnalyzePresentationAsync(
        IReadOnlyList<string> categoryNames,
        string fromYm,
        string toYm,
        IReadOnlyList<IanReportPoint> points,
        string fallbackChart,
        CancellationToken ct)
    {
        var context = new
        {
            fromYm,
            toYm,
            accounts = categoryNames,
            fallbackChartKind = fallbackChart,
            points = points.Select(p => new { p.Key, p.Label, amount = p.Amount }).ToList(),
            total = points.Sum(p => p.Amount),
        };
        var prompt = JsonSerializer.Serialize(context);
        var raw = await _gemini.GenerateJsonAsync(AnalyzePresentationInstruction, prompt, ct);
        if (raw is null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            return new AnalyzePresentation(
                ReadString(root, "chartKind"),
                ReadString(root, "title"),
                ReadString(root, "insight"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildLocalInsight(int categoryCount, IReadOnlyList<IanReportPoint> points, string fromYm, string toYm)
    {
        var total = points.Sum(p => p.Amount);
        if (categoryCount == 1)
        {
            var peak = points.OrderByDescending(p => Math.Abs(p.Amount)).FirstOrDefault();
            return peak is null
                ? $"Total de {FormatMoney(total)} entre {fromYm} e {toYm}."
                : $"Total de {FormatMoney(total)} no período. Maior competência: {peak.Label} ({FormatMoney(peak.Amount)}).";
        }

        var top = points.Where(p => p.Amount != 0).OrderByDescending(p => Math.Abs(p.Amount)).FirstOrDefault();
        return top is null
            ? $"Total de {FormatMoney(total)} entre {fromYm} e {toYm}."
            : $"Total de {FormatMoney(total)}. Maior conta: {top.Label} ({FormatMoney(top.Amount)}).";
    }

    private static IReadOnlyList<Guid> ParseCategoryIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return [];
        var list = new List<Guid>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(part, out var id) && id != Guid.Empty)
                list.Add(id);
        }

        return list.Distinct().Take(MaxAnalyzeCategories).ToList();
    }

    private static string? NormalizeChartKind(string? chartKind)
    {
        if (string.IsNullOrWhiteSpace(chartKind))
            return null;
        return chartKind.Trim().ToLowerInvariant() switch
        {
            IanChartSelector.Line or "linha" => IanChartSelector.Line,
            IanChartSelector.Pie or "pizza" => IanChartSelector.Pie,
            IanChartSelector.Bar or "barra" or "barras" => IanChartSelector.Bar,
            IanChartSelector.None => IanChartSelector.None,
            _ => null,
        };
    }

    private static string TruncateInsight(string insight)
        => insight.Length <= MaxInsightLength ? insight : insight[..MaxInsightLength].TrimEnd() + "…";

    private sealed record AnalyzePresentation(string? ChartKind, string? Title, string? Insight);

    private static string FormatMoney(decimal amount)
        => amount.ToString("C", CultureInfo.GetCultureInfo("pt-BR"));

    private async Task<IReadOnlyList<Entry>> LoadMatchedEntriesAsync(
        IanReportQuery query,
        string fromYm,
        string toYm,
        CancellationToken ct)
    {
        var entries = await _repo.ListAsync<Entry>(
            e => e.CompetenceYm.CompareTo(fromYm) >= 0
                 && e.CompetenceYm.CompareTo(toYm) <= 0
                 && (e.Type == EntryType.Expense
                     || e.Type == EntryType.Income
                     || e.Type == EntryType.Contribution),
            ct);

        if (query.MatchKind is null || query.MatchId is null)
        {
            if (string.IsNullOrWhiteSpace(query.Subject))
                return entries;
            var needle = IanSubjectMatcher.Normalize(query.Subject);
            return entries.Where(e => IanSubjectMatcher.Matches(needle, e.Description)).ToList();
        }

        return query.MatchKind switch
        {
            IanSubjectMatcher.MatchCategory => entries.Where(e => e.CategoryId == query.MatchId).ToList(),
            IanSubjectMatcher.MatchAccount => entries.Where(e => e.AccountId == query.MatchId || e.ContraAccountId == query.MatchId).ToList(),
            IanSubjectMatcher.MatchDescription => await FilterByDescriptionIdAsync(entries, query.MatchId.Value, query.Subject, ct),
            _ => entries,
        };
    }

    private async Task<IReadOnlyList<Entry>> FilterByDescriptionIdAsync(
        IReadOnlyList<Entry> entries,
        Guid entryId,
        string? subject,
        CancellationToken ct)
    {
        var sample = entries.FirstOrDefault(e => e.Id == entryId)
            ?? await _repo.GetAsync<Entry>(entryId, ct);
        var label = sample?.Description ?? subject;
        if (string.IsNullOrWhiteSpace(label))
            return [];
        var needle = IanSubjectMatcher.Normalize(label);
        return entries.Where(e => IanSubjectMatcher.Matches(needle, e.Description)).ToList();
    }

    private async Task<IReadOnlyList<IanMatchCandidate>> FindSubjectMatchesAsync(string subject, CancellationToken ct)
    {
        var categories = (await _categories.ListAsync(null, true, true, ct))
            .Select(c => (c.Id, c.Name));
        var accounts = (await _repo.ListAsync<Account>(a => !a.IsArchived, ct))
            .Select(a => (a.Id, a.Name));
        var entries = await _repo.ListAsync<Entry>(null, ct);
        var descriptions = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Description))
            .GroupBy(e => IanSubjectMatcher.Normalize(e.Description))
            .Select(g => g.OrderByDescending(e => e.OccurredAt).First())
            .Select(e => (e.Id, e.Description));

        return IanSubjectMatcher.Find(subject, categories, descriptions, accounts);
    }

    private async Task<IanIntent?> ClassifyAsync(string question, string fallbackYm, CancellationToken ct)
    {
        var prompt =
            $"Competência atual do app: {fallbackYm}. Ano civil atual: {DateTime.UtcNow.Year}.\n" +
            $"Pergunta: {question}";
        var raw = await _gemini.GenerateJsonAsync(IntentInstruction, prompt, ct);
        if (raw is not null)
        {
            var parsed = TryParseIntent(raw);
            if (parsed is not null)
                return parsed;
        }

        return FromHeuristic(IanQuestionClassifier.Classify(question, fallbackYm));
    }

    private static IanIntent? TryParseIntent(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            string? kind = null;
            if (root.TryGetProperty("kind", out var kindEl))
            {
                kind = kindEl.ValueKind switch
                {
                    JsonValueKind.String => kindEl.GetString(),
                    JsonValueKind.Null => null,
                    _ => null,
                };
            }

            if (string.IsNullOrWhiteSpace(kind))
                return new IanIntent(null, null, null, null, null, null);

            return new IanIntent(
                kind,
                ReadString(root, "competenceYm"),
                ReadString(root, "fromYm"),
                ReadString(root, "toYm"),
                ReadString(root, "subject"),
                ReadString(root, "focus"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var value = el.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Prefer the local classifier for clear cases (named subject, month composition)
    /// so Gemini cannot demote them to a generic snapshot.
    /// </summary>
    private static IanIntent RefineIntent(string question, IanIntent? intent, string fallbackYm)
    {
        var heuristic = IanQuestionClassifier.Classify(question, fallbackYm);

        if (heuristic.Kind == IanChartSelector.KindSeries
            && !string.IsNullOrWhiteSpace(heuristic.Subject))
            return FromHeuristic(heuristic);

        if (heuristic.Kind == IanChartSelector.KindComposition
            && heuristic.Focus is IanQuestionClassifier.FocusExpense or IanQuestionClassifier.FocusIncome)
            return FromHeuristic(heuristic);

        if (intent is not null
            && string.Equals(NormalizeKind(intent.Kind), IanChartSelector.KindSeries, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(intent.Subject)
            && !string.IsNullOrWhiteSpace(heuristic.Subject))
            return FromHeuristic(heuristic);

        return intent ?? FromHeuristic(heuristic);
    }

    private static IanIntent FromHeuristic(IanQuestionClassifier.Result heuristic)
        => new(heuristic.Kind, heuristic.CompetenceYm, heuristic.FromYm, heuristic.ToYm, heuristic.Subject, heuristic.Focus);

    private static string? NormalizeFocus(string? focus)
    {
        if (string.IsNullOrWhiteSpace(focus))
            return null;
        return focus.Trim().ToLowerInvariant() switch
        {
            IanQuestionClassifier.FocusExpense or "despesa" or "despesas" or "gasto" or "gastos" => IanQuestionClassifier.FocusExpense,
            IanQuestionClassifier.FocusIncome or "receita" or "receitas" or "renda" => IanQuestionClassifier.FocusIncome,
            _ => null,
        };
    }

    private static (string FromYm, string ToYm) ResolveRange(string kind, string? fromYm, string? toYm, string competenceYm)
    {
        if (kind != IanChartSelector.KindSeries)
            return (competenceYm, competenceYm);

        var from = Competence.IsValid(fromYm) ? fromYm! : $"{DateTime.UtcNow.Year:D4}-01";
        var to = Competence.IsValid(toYm) ? toYm! : competenceYm;
        return (from, to);
    }

    private static string? NormalizeKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return null;
        return kind.Trim().ToLowerInvariant() switch
        {
            IanChartSelector.KindSnapshot or "month" or "resumo" => IanChartSelector.KindSnapshot,
            IanChartSelector.KindSeries or "evolution" or "time" => IanChartSelector.KindSeries,
            IanChartSelector.KindComposition or "pie" or "parts" => IanChartSelector.KindComposition,
            IanChartSelector.KindAnalyze or "analise" or "análise" => IanChartSelector.KindAnalyze,
            _ => null,
        };
    }

    private static string LabelForMatchKind(string matchKind) => matchKind switch
    {
        IanSubjectMatcher.MatchCategory => "categoria",
        IanSubjectMatcher.MatchDescription => "lançamento",
        IanSubjectMatcher.MatchAccount => "conta",
        _ => matchKind,
    };

    private async Task<bool> IsRateLimitedAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        var used = await _repo.CountAsync<AuditEvent>(
            e => (e.EntityName == "Gemini" || e.EntityName == "Ian") && e.TimestampUtc > cutoff,
            ct);
        return used >= MaxQuestionsPerHour;
    }

    private async Task RecordUsageAsync(int length, bool ok, CancellationToken ct)
    {
        AuditRecorder.Record(_audits, _correlation, "Ian", Guid.NewGuid(), "IanUsed", new { Length = length, Ok = ok });
        await _uow.SaveChangesAsync(ct);
    }

    private sealed record IanIntent(
        string? Kind,
        string? CompetenceYm,
        string? FromYm,
        string? ToYm,
        string? Subject,
        string? Focus);
}
