using System.Globalization;
using System.Text.RegularExpressions;

namespace Conora.Domain.Services;

/// <summary>Local fallback when Gemini is off or returns nothing useful.</summary>
public static partial class IanQuestionClassifier
{
    public const string FocusExpense = "expense";
    public const string FocusIncome = "income";

    public sealed record Result(
        string? Kind,
        string? CompetenceYm,
        string? FromYm,
        string? ToYm,
        string? Subject,
        string? Focus);

    public static Result Classify(string question, string fallbackYm)
    {
        var q = IanSubjectMatcher.Normalize(question);
        var year = DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture);
        var yearStart = $"{year}-01";
        var yearEnd = Competence.From(DateTime.UtcNow);
        var wantsExpense = MentionsExpense(q);
        var wantsIncome = MentionsIncome(q);
        var wantsYear = MentionsYear(q);
        var subject = ExtractSubject(question);

        // Named subject (account / category / entry) always wins over generic snapshot.
        if (subject is not null)
        {
            return new Result(
                IanChartSelector.KindSeries,
                fallbackYm,
                yearStart,
                yearEnd,
                subject,
                InferFocusFromQuestion(wantsExpense, wantsIncome));
        }

        if (wantsYear && (q.Contains("evoluc") || q.Contains("ao longo") || q.Contains("serie")))
        {
            return new Result(
                IanChartSelector.KindSeries,
                fallbackYm,
                yearStart,
                yearEnd,
                null,
                InferFocusFromQuestion(wantsExpense, wantsIncome));
        }

        // Receita × despesa only when both sides are asked and nothing is named.
        if (IsIncomeVsExpenseCompare(q, wantsExpense, wantsIncome))
            return new Result(IanChartSelector.KindSnapshot, fallbackYm, null, null, null, null);

        // "despesas / gastos do mês" → pie by category
        if (wantsExpense && !wantsIncome
            && (MentionsMonth(q) || q.Contains("como esta") || q.Contains("como estamos") || q.Contains("mostre") || q.Contains("mostra")))
        {
            return new Result(IanChartSelector.KindComposition, fallbackYm, null, null, null, FocusExpense);
        }

        if (wantsIncome && !wantsExpense
            && (MentionsMonth(q) || q.Contains("como esta") || q.Contains("mostre") || q.Contains("mostra")))
        {
            return new Result(IanChartSelector.KindComposition, fallbackYm, null, null, null, FocusIncome);
        }

        if (q.Contains("composic") || q.Contains("onde gastei") || q.Contains("distribuic") || q.Contains("por categoria"))
        {
            return new Result(
                IanChartSelector.KindComposition,
                fallbackYm,
                null,
                null,
                null,
                wantsExpense ? FocusExpense : wantsIncome ? FocusIncome : FocusExpense);
        }

        if (q.Contains("meu mes") || q.Contains("como esta") || q.Contains("como estamos") || q.Contains("resumo"))
            return new Result(IanChartSelector.KindSnapshot, fallbackYm, null, null, null, null);

        return new Result(IanChartSelector.KindSnapshot, fallbackYm, null, null, null, null);
    }

    public static bool MentionsExpense(string normalizedQuestion)
        => normalizedQuestion.Contains("despesa")
           || normalizedQuestion.Contains("gasto")
           || normalizedQuestion.Contains("gastei")
           || normalizedQuestion.Contains("gastos")
           || normalizedQuestion.Contains("paguei");

    public static bool MentionsIncome(string normalizedQuestion)
        => normalizedQuestion.Contains("receita")
           || normalizedQuestion.Contains("renda")
           || normalizedQuestion.Contains("salario")
           || normalizedQuestion.Contains("recebi")
           || normalizedQuestion.Contains("recebimento");

    public static bool MentionsMonth(string normalizedQuestion)
        => normalizedQuestion.Contains("mes")
           || normalizedQuestion.Contains("competencia");

    public static bool MentionsYear(string normalizedQuestion)
        => normalizedQuestion.Contains("este ano")
           || normalizedQuestion.Contains("neste ano")
           || normalizedQuestion.Contains("desse ano")
           || normalizedQuestion.Contains("deste ano")
           || normalizedQuestion.Contains("nesse ano")
           || normalizedQuestion.Contains("no ano")
           || normalizedQuestion.Contains("do ano")
           || normalizedQuestion.Contains("ao longo do ano")
           || YearTokenRegex().IsMatch(normalizedQuestion);

    /// <summary>Pulls a named subject from many phrasings (quotes, na/da conta, quanto recebi…).</summary>
    public static string? ExtractSubject(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
            return null;

        var quoted = QuotedSubjectRegex().Match(question);
        if (quoted.Success)
            return CleanSubject(quoted.Groups[1].Value);

        foreach (Match match in AnchoredSubjectRegex().Matches(question))
        {
            var candidate = CleanSubject(match.Groups["name"].Value);
            if (candidate is not null)
                return candidate;
        }

        return null;
    }

    private static bool IsIncomeVsExpenseCompare(string q, bool wantsExpense, bool wantsIncome)
    {
        if (!wantsExpense || !wantsIncome)
            return false;

        return q.Contains("compara")
               || q.Contains("comparar")
               || q.Contains("versus")
               || q.Contains(" x ")
               || q.Contains(" vs ")
               || (q.Contains("receita") && q.Contains("despesa"))
               || (q.Contains("recebi") && q.Contains("paguei"));
    }

    private static string? InferFocusFromQuestion(bool wantsExpense, bool wantsIncome)
    {
        if (wantsExpense && !wantsIncome)
            return FocusExpense;
        if (wantsIncome && !wantsExpense)
            return FocusIncome;
        return null;
    }

    private static string? CleanSubject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var rest = raw.Trim().Trim('"', '\'', '“', '”', '«', '»');
        foreach (var stop in new[]
                 {
                     " neste ano", " deste ano", " desse ano", " nesse ano", " este ano", " no ano", " do ano",
                     " neste mes", " deste mes", " esse mes", " este mes", " do mes", " no mes",
                     " neste mês", " deste mês", " esse mês", " este mês", " do mês", " no mês",
                     " no periodo", " no período", " ?",
                 })
        {
            var idx = rest.IndexOf(stop, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
                rest = rest[..idx];
        }

        rest = rest.Trim().TrimEnd('.', '?', '!', ',', ';', ':').Trim();
        if (rest.Length < 2)
            return null;

        var normalized = IanSubjectMatcher.Normalize(rest);
        // Reject bare focus words — keep real names that contain "salario", "receita", etc.
        if (normalized is "receita" or "receitas" or "despesa" or "despesas" or "gasto" or "gastos"
            or "mes" or "ano" or "conta" or "categoria")
            return null;

        // "receita e ... despesa" is a compare, not a named subject.
        if ((normalized.Contains("receita") || normalized.Contains("recebi"))
            && (normalized.Contains("despesa") || normalized.Contains("paguei") || normalized.Contains("gasto")))
            return null;

        return rest;
    }

    [GeneratedRegex("""["“”']([^"“”']{2,80})["“”']""", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedSubjectRegex();

    /// <summary>
    /// Captures the name after common anchors. Stops before year/month tails when present.
    /// </summary>
    [GeneratedRegex(
        """(?i)(?:\b(?:na|da|do|de|em|pela|pelo)\s+(?:conta|categoria|lancamento|lançamento)\s+|evolu[cç][aã]o\s+(?:da|do|de)\s+|(?:quanto\s+)?(?:eu\s+)?(?:recebi|paguei|gastei)\s+(?:na|da|de|em|do)\s+(?:conta\s+|categoria\s+)?)(?<name>.+?)(?=\s+e\s+quanto\b|\s+(?:neste|deste|desse|nesse|este|no|do)\s+ano\b|\s+(?:neste|deste|esse|este|do|no)\s+m[eê]s\b|\s*\?|\s*$)""",
        RegexOptions.CultureInvariant)]
    private static partial Regex AnchoredSubjectRegex();

    [GeneratedRegex("""\b(19|20)\d{2}\b""", RegexOptions.CultureInvariant)]
    private static partial Regex YearTokenRegex();
}
