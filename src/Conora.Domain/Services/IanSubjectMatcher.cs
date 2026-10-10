using System.Globalization;
using System.Text;

namespace Conora.Domain.Services;

public sealed record IanMatchCandidate(string MatchKind, Guid Id, string Label);

/// <summary>
/// Resolves a free-text subject against the user's data.
/// Order: category name, then entry description, then bank account name.
/// </summary>
public static class IanSubjectMatcher
{
    public const string MatchCategory = "category";
    public const string MatchDescription = "description";
    public const string MatchAccount = "account";

    public static IReadOnlyList<IanMatchCandidate> Find(
        string subject,
        IEnumerable<(Guid Id, string Name)> categories,
        IEnumerable<(Guid Id, string Description)> entryDescriptions,
        IEnumerable<(Guid Id, string Name)> accounts)
    {
        var needle = Normalize(subject);
        if (needle.Length == 0)
            return [];

        var results = new List<IanMatchCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (id, name) in categories)
        {
            if (!Matches(needle, name))
                continue;
            var key = $"{MatchCategory}:{id}";
            if (!seen.Add(key))
                continue;
            results.Add(new IanMatchCandidate(MatchCategory, id, name.Trim()));
        }

        foreach (var (id, description) in entryDescriptions)
        {
            if (!Matches(needle, description))
                continue;
            var label = description.Trim();
            var key = $"{MatchDescription}:{Normalize(label)}";
            if (!seen.Add(key))
                continue;
            results.Add(new IanMatchCandidate(MatchDescription, id, label));
        }

        foreach (var (id, name) in accounts)
        {
            if (!Matches(needle, name))
                continue;
            var key = $"{MatchAccount}:{id}";
            if (!seen.Add(key))
                continue;
            results.Add(new IanMatchCandidate(MatchAccount, id, name.Trim()));
        }

        return results;
    }

    public static bool Matches(string normalizedNeedle, string? haystack)
    {
        var h = Normalize(haystack);
        return h.Length > 0 && h.Contains(normalizedNeedle, StringComparison.Ordinal);
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var form = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var chars = form.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
        return string.Concat(chars).Normalize(NormalizationForm.FormC);
    }
}
