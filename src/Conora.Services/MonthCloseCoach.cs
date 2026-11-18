using System.Globalization;
using Conora.Domain.Enums;
using Conora.Services.Contracts;

namespace Conora.Services;

/// <summary>
/// Picks a single actionable month-close insight (overrun, unposted, or anomaly).
/// Debt staging stays in <see cref="PendingDebtService"/> — this only builds context.
/// </summary>
public static class MonthCloseCoach
{
    public const string AlertCode = "MONTH_CLOSE_COACH";
    public const string SentCode = "MONTH_CLOSE_COACH_SENT";

    public enum Kind
    {
        Overrun,
        Unposted,
        Anomaly
    }

    public sealed record Line(
        Guid? CategoryId,
        string CategoryName,
        CategorySection Section,
        decimal PlannedAmount,
        decimal ActualAmount,
        bool IsGroup);

    public sealed record Insight(Kind Kind, string Message, Guid? CategoryId);

    private static readonly CategorySection[] ExpenseSections =
    [
        CategorySection.Discount,
        CategorySection.Debt,
        CategorySection.Essential,
        CategorySection.LifeProject,
        CategorySection.Social
    ];

    public static IReadOnlyList<Line> FromBudget(BudgetResponse budget)
        => budget.Lines
            .Where(l => !l.IsGroup && l.CategoryId is not null && ExpenseSections.Contains(l.Section))
            .Select(l => new Line(
                l.CategoryId,
                l.CategoryName,
                l.Section,
                l.PlannedAmount,
                l.ActualAmount,
                l.IsGroup))
            .ToList();

    /// <summary>
    /// One insight max. Priority: overrun → unposted (planned with zero actual) → anomaly vs previous month.
    /// </summary>
    public static Insight? Pick(
        string monthLabel,
        IReadOnlyList<Line> lines,
        IReadOnlyDictionary<Guid, decimal>? previousActuals = null,
        CultureInfo? culture = null)
    {
        culture ??= CultureInfo.GetCultureInfo("pt-BR");
        var analytical = lines.Where(l => !l.IsGroup && l.CategoryId is not null).ToList();
        if (analytical.Count == 0)
            return null;

        var overrun = analytical
            .Where(l => l.PlannedAmount > 0 && l.ActualAmount > l.PlannedAmount)
            .Select(l => (line: l, over: l.ActualAmount - l.PlannedAmount))
            .OrderByDescending(x => x.over)
            .FirstOrDefault();
        if (overrun.line is not null && overrun.over >= 1m)
        {
            return new Insight(
                Kind.Overrun,
                $"{overrun.line.CategoryName} passou {Money(overrun.over, culture)} do planejado em {monthLabel} — no próximo mês vale ajustar o limite ou cortar um pouco ali.",
                overrun.line.CategoryId);
        }

        var unposted = analytical
            .Where(l => l.PlannedAmount > 0 && l.ActualAmount == 0)
            .OrderByDescending(l => l.PlannedAmount)
            .FirstOrDefault();
        if (unposted is not null)
        {
            return new Insight(
                Kind.Unposted,
                $"{unposted.CategoryName} ficou com {Money(unposted.PlannedAmount, culture)} planejados e zero lançamento em {monthLabel} — confere se faltou registrar ou se o valor pode baixar.",
                unposted.CategoryId);
        }

        if (previousActuals is { Count: > 0 })
        {
            var anomaly = analytical
                .Where(l => l.CategoryId is Guid id
                            && l.ActualAmount >= 50m
                            && previousActuals.TryGetValue(id, out var prev)
                            && prev >= 20m
                            && l.ActualAmount >= prev * 1.5m)
                .Select(l =>
                {
                    var prev = previousActuals[l.CategoryId!.Value];
                    return (line: l, delta: l.ActualAmount - prev);
                })
                .OrderByDescending(x => x.delta)
                .FirstOrDefault();

            if (anomaly.line is not null && anomaly.delta >= 50m)
            {
                return new Insight(
                    Kind.Anomaly,
                    $"{anomaly.line.CategoryName} subiu {Money(anomaly.delta, culture)} em relação ao mês anterior — um olhar rápido no Raio-X ajuda a ver o que puxou.",
                    anomaly.line.CategoryId);
            }
        }

        return null;
    }

    /// <summary>
    /// Short conversational pre-close reading (one paragraph). No tables; names over loose amounts.
    /// </summary>
    public static string BuildPreviewSummary(
        string monthLabel,
        decimal totalPlanned,
        decimal totalActual,
        IReadOnlyList<Line> lines,
        int openGapCount,
        decimal openGapTotal,
        IReadOnlyDictionary<Guid, decimal>? previousActuals = null,
        CultureInfo? culture = null)
    {
        culture ??= CultureInfo.GetCultureInfo("pt-BR");
        var shortMonth = ShortMonth(monthLabel);

        if (totalPlanned <= 0)
        {
            return openGapCount > 0
                ? $"Em {shortMonth} ainda não tem orçamento montado. Tem conta em aberto pra marcar como dívida antes de fechar — o restante continua sobra."
                : $"Em {shortMonth} ainda não tem orçamento montado. Dá pra fechar assim, mas o ritual fica mais claro quando o mês tem previsto.";
        }

        var within = totalActual <= totalPlanned;
        var insight = Pick(monthLabel, lines, previousActuals, culture);
        var category = insight?.CategoryId is Guid id
            ? lines.FirstOrDefault(l => l.CategoryId == id)?.CategoryName ?? "essa conta"
            : "essa conta";
        var tip = openGapCount switch
        {
            0 => " Se estiver tudo certo, pode fechar o mês.",
            1 => openGapTotal > 0
                ? " Antes de fechar, marca abaixo se essa conta em aberto é dívida — senão continua sobra."
                : " Antes de fechar, marca abaixo o que é dívida — o que ficar sem marca continua sobra.",
            _ => " Antes de fechar, marca abaixo o que é dívida — o que ficar sem marca continua sobra."
        };

        if (within)
        {
            return insight?.Kind switch
            {
                Kind.Overrun =>
                    $"Você segurou bem o orçamento em {shortMonth}, só escorregou em {category}.{tip}",
                Kind.Unposted =>
                    $"{Capitalize(shortMonth)} ficou dentro do planejado, e {category} ainda aparece com previsto sem lançamento.{tip}",
                Kind.Anomaly =>
                    $"{Capitalize(shortMonth)} ficou dentro do orçamento, mas {category} puxou mais que no mês passado.{tip}",
                _ =>
                    $"{Capitalize(shortMonth)} fechou redondo no orçamento.{tip}"
            };
        }

        return insight?.Kind switch
        {
            Kind.Overrun =>
                $"{Capitalize(shortMonth)} ficou acima do planejado, e o maior escorregão foi em {category}.{tip}",
            Kind.Unposted =>
                $"{Capitalize(shortMonth)} passou do orçamento; {category} ficou com previsto sem lançamento — vale conferir.{tip}",
            Kind.Anomaly =>
                $"{Capitalize(shortMonth)} passou do orçamento, e {category} saltou em relação ao mês anterior.{tip}",
            _ =>
                $"{Capitalize(shortMonth)} passou do orçamento.{tip}"
        };
    }

    private static string ShortMonth(string monthLabel)
    {
        var slash = monthLabel.IndexOf('/');
        return slash > 0 ? monthLabel[..slash].ToLowerInvariant() : monthLabel.ToLowerInvariant();
    }

    private static string Capitalize(string value)
        => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string Money(decimal value, CultureInfo culture)
        => $"R$ {Math.Round(value, MidpointRounding.AwayFromZero).ToString("N0", culture)}";
}
