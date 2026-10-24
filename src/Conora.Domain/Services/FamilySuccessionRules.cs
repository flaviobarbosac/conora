namespace Conora.Domain.Services;

/// <summary>
/// Family administration handover. User-facing text stays on the public 24-hour window
/// and the day 1–5 request window. The extra restore allowance is not for display.
/// </summary>
public static class FamilySuccessionRules
{
    public const string OutsideWindowMessage =
        "A transferência de administração só pode ser feita do dia 1 ao dia 5, depois do fechamento do mês.";

    public const string PublicRestoreMessage =
        "Depois do cancelamento na loja, a nova responsável tem 24 horas para assinar e o grupo seguir com ela. Se esse prazo passar, o grupo é desfeito e os dados continuam salvos.";

    private static readonly TimeSpan BrazilOffset = TimeSpan.FromHours(-3);
    private static readonly TimeSpan PublicWindow = TimeSpan.FromHours(24);

    public readonly record struct PlannedItem(string Month, string Key, decimal Amount);

    public enum Outcome
    {
        Wait,
        Restore,
        Dissolve
    }

    public static DateTime ToBrazil(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(BrazilOffset);

    public static bool CanRequest(DateTime utcNow)
    {
        var day = ToBrazil(utcNow).Day;
        return day is >= 1 and <= 5;
    }

    public static string HandoverMonth(DateTime utcNow) => Competence.From(ToBrazil(utcNow));

    public static bool IsHandoverOrLater(string month, string handoverMonth) =>
        Competence.Compare(month, handoverMonth) >= 0;

    /// <summary>
    /// Successor book after the store cancellation. Months before the handover stay as they were.
    /// From the handover month forward, her plan is replaced by every planned month he had.
    /// </summary>
    public static IReadOnlyList<PlannedItem> MigrateBudgets(
        IReadOnlyList<PlannedItem> successor,
        IReadOnlyList<PlannedItem> predecessor,
        string handoverMonth)
    {
        var past = successor.Where(item => !IsHandoverOrLater(item.Month, handoverMonth));
        var moved = predecessor.Where(item => IsHandoverOrLater(item.Month, handoverMonth));
        return past.Concat(moved).OrderBy(item => item.Month, StringComparer.Ordinal).ThenBy(item => item.Key, StringComparer.Ordinal).ToList();
    }

    public static DateTime InternalDeadlineUtc(DateTime cancelledAtUtc)
    {
        var local = ToBrazil(cancelledAtUtc);
        var publicDeadline = cancelledAtUtc.Add(PublicWindow);
        if (local.Day != 5)
            return publicDeadline;

        var endOfWindowLocal = new DateTime(local.Year, local.Month, 5, 23, 59, 59, DateTimeKind.Unspecified);
        var endOfWindowUtc = DateTime.SpecifyKind(endOfWindowLocal - BrazilOffset, DateTimeKind.Utc);
        var extended = endOfWindowUtc.AddHours(48);
        return extended > publicDeadline ? extended : publicDeadline;
    }

    public static Outcome Decide(DateTime cancelledAtUtc, DateTime nowUtc, bool successorSubscribed)
    {
        if (nowUtc <= InternalDeadlineUtc(cancelledAtUtc))
            return successorSubscribed ? Outcome.Restore : Outcome.Wait;
        return Outcome.Dissolve;
    }
}
