namespace Conora.Domain.Services;

/// <summary>Day 1–5 in America/Sao_Paulo: remind the budget owner to close the previous month.</summary>
public static class MonthCloseReminder
{
    private static readonly TimeZoneInfo Brazil =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "E. South America Standard Time" : "America/Sao_Paulo");

    public static bool IsActive(DateTime utcNow)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), Brazil);
        return local.Day is >= 1 and <= 5;
    }
}
