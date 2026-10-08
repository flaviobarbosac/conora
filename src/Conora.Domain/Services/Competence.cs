using System.Globalization;
using Conora.Domain.Exceptions;

namespace Conora.Domain.Services;

/// <summary>Helpers for the "yyyy-MM" competence string used across the finance domain.</summary>
public static class Competence
{
    private const string Format = "yyyy-MM";

    public static bool IsValid(string? ym)
        => ym is { Length: 7 }
           && DateTime.TryParseExact(ym, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static string From(DateTime date) => date.ToString(Format, CultureInfo.InvariantCulture);

    public static string Require(string? ym, string field = "competenceYm")
    {
        if (!IsValid(ym))
            throw new ValidationException(field, "Competência deve estar no formato yyyy-MM.");

        return ym!;
    }

    public static string AddMonths(string ym, int months) => From(Start(ym).AddMonths(months));

    public static int Compare(string leftYm, string rightYm) => string.CompareOrdinal(Require(leftYm), Require(rightYm));

    public static IReadOnlyList<string> RangeInclusive(string fromYm, string toYm, string fromField = "contributionStartYm", string toField = "dueDate")
    {
        var current = Require(fromYm, fromField);
        var end = Require(toYm, toField);
        if (Compare(current, end) > 0)
            throw new ValidationException(fromField, "O início do aporte não pode ser depois do prazo.");

        var months = new List<string>();
        while (true)
        {
            months.Add(current);
            if (current == end)
                break;
            current = AddMonths(current, 1);
        }

        return months;
    }

    public static DateTime Start(string ym)
    {
        var parsed = DateTime.ParseExact(ym, Format, CultureInfo.InvariantCulture);
        return new DateTime(parsed.Year, parsed.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    public static DateTime EndExclusive(string ym) => Start(ym).AddMonths(1);

    /// <summary>Normalizes any date to UTC kind keeping the calendar components (Npgsql requires UTC).</summary>
    public static DateTime ToUtc(DateTime date)
        => date.Kind == DateTimeKind.Utc ? date : DateTime.SpecifyKind(date, DateTimeKind.Utc);
}
