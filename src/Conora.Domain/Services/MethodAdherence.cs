namespace Conora.Domain.Services;

/// <summary>Same ceiling rule as the month close: planned above zero and spent not over it.</summary>
public static class MethodAdherence
{
    public static bool IsWithin(decimal planned, decimal spent) => planned > 0m && spent <= planned;

    public static int CountUsersWithStreak(
        IReadOnlyList<(Guid UserId, string CompetenceYm)> closedMonths,
        IReadOnlyDictionary<(Guid UserId, string CompetenceYm), (decimal Planned, decimal Spent)> cells,
        int length = 3)
    {
        if (length < 1 || closedMonths.Count == 0)
            return 0;

        var total = 0;
        foreach (var group in closedMonths.GroupBy(row => row.UserId))
        {
            var months = group.Select(row => row.CompetenceYm).Distinct().OrderBy(ym => ym, StringComparer.Ordinal).ToList();
            var run = 0;
            string? previous = null;
            var best = 0;
            foreach (var ym in months)
            {
                var within = cells.TryGetValue((group.Key, ym), out var cell) && IsWithin(cell.Planned, cell.Spent);
                var consecutive = previous is not null && Competence.AddMonths(previous, 1) == ym;
                if (within && consecutive && run > 0)
                    run++;
                else
                    run = within ? 1 : 0;

                if (run > best)
                    best = run;
                previous = ym;
            }

            if (best >= length)
                total++;
        }

        return total;
    }
}
