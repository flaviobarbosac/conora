using System.Globalization;
using Conora.Domain.Catalog;
using Conora.Domain.Enums;

namespace Conora.Domain.Services;

/// <summary>
/// Phrases an answer from Raio-X numbers the caller already loaded. Does not query or recompute them.
/// </summary>
public static class IanInsight
{
    public readonly record struct MonthSlice(
        decimal Received,
        decimal Discount,
        decimal Life,
        decimal Essential,
        decimal Social);

    public readonly record struct AccountSlice(string Name, decimal Amount);

    public static bool LooksLike(string? transcript)
    {
        var text = IanSubjectMatcher.Normalize(transcript);
        if (text.Length == 0)
            return false;
        if (text.Contains("abre") || text.Contains("abrir") || text.Contains("abra")
            || text.Contains("ir pra") || text.Contains("ir para") || text.Contains("vai pra") || text.Contains("vai para"))
            return false;

        return text.Contains("vaz")
            || text.Contains("analis")
            || text.Contains("detalh")
            || text.Contains("aprofund")
            || text.Contains("para onde")
            || text.Contains("situacao")
            || text.Contains("como estamos")
            || text.Contains("como esta meu mes")
            || text.Contains("como esta o mes")
            || text.Contains("raio-x")
            || text.Contains("raio x")
            || text.Contains("raiox")
            || (text.Contains("ultim") && text.Contains("mes"));
    }

    public static int MonthCount(string? transcript)
    {
        var text = IanSubjectMatcher.Normalize(transcript);
        if (text.Contains("semestre"))
            return 6;
        if (!text.Contains("ultim"))
            return 1;

        for (var n = 12; n >= 1; n--)
        {
            if (text.Contains(" " + n.ToString(CultureInfo.InvariantCulture) + " ", StringComparison.Ordinal)
                || text.EndsWith(" " + n.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                return n;
        }

        foreach (var (word, count) in NumberWords)
        {
            if (text.Contains(" " + word + " ", StringComparison.Ordinal) || text.EndsWith(" " + word, StringComparison.Ordinal))
                return count;
        }

        return 6;
    }

    public static CategorySection? Focus(string? transcript)
    {
        var text = IanSubjectMatcher.Normalize(transcript);
        if (text.Contains("essencial"))
            return CategorySection.Essential;
        if (text.Contains("social"))
            return CategorySection.Social;
        if (text.Contains("desconto"))
            return CategorySection.Discount;
        if (text.Contains("projeto"))
            return CategorySection.LifeProject;
        return null;
    }

    public static string DescribeLeak(IReadOnlyList<MonthSlice> months)
    {
        var period = Period(months.Count);
        var received = months.Sum(m => m.Received);
        var ranked = new (CategorySection Section, decimal Amount)[]
        {
            (CategorySection.Discount, months.Sum(m => m.Discount)),
            (CategorySection.LifeProject, months.Sum(m => m.Life)),
            (CategorySection.Essential, months.Sum(m => m.Essential)),
            (CategorySection.Social, months.Sum(m => m.Social)),
        }
        .Select(item => (item.Section, Amount: Math.Abs(item.Amount)))
        .Where(item => item.Amount > 0)
        .OrderByDescending(item => item.Amount)
        .ToList();

        if (ranked.Count == 0)
            return $"{period}, a renda recebida foi {Money(received)} e não há saída lançada.";

        var lead = ranked[0];
        var rest = ranked.Skip(1).Select(item => $"{SystemCategories.SectionLabel(item.Section)}, {Money(item.Amount)}");
        var tail = rest.Any() ? " Depois, " + string.Join("; ", rest) + "." : "";
        return $"{period}, a renda recebida foi {Money(received)}. A maior parte foi para {SystemCategories.SectionLabel(lead.Section)}, {Money(lead.Amount)}.{tail}";
    }

    public static string DescribeSection(CategorySection section, IReadOnlyList<AccountSlice> accounts, int monthCount)
    {
        var period = Period(monthCount);
        var label = SystemCategories.SectionLabel(section);
        var ranked = accounts
            .Select(item => (item.Name, Amount: Math.Abs(item.Amount)))
            .Where(item => item.Amount > 0 && !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Name: group.Key, Amount: group.Sum(item => item.Amount)))
            .OrderByDescending(item => item.Amount)
            .Take(3)
            .ToList();

        if (ranked.Count == 0)
            return $"{period}, não há valor em {label}.";

        var lead = ranked[0];
        var rest = ranked.Skip(1).Select(item => $"{item.Name}, {Money(item.Amount)}");
        var tail = rest.Any() ? " Depois, " + string.Join("; ", rest) + "." : "";
        return $"{period}, em {label}, o maior valor foi {lead.Name}, {Money(lead.Amount)}.{tail}";
    }

    private static string Period(int monthCount)
        => monthCount <= 1 ? "Neste mês" : $"Nos últimos {monthCount} meses";

    private static string Money(decimal value)
        => value.ToString("C", CultureInfo.GetCultureInfo("pt-BR"));

    private static readonly (string Word, int Count)[] NumberWords =
    [
        ("doze", 12),
        ("onze", 11),
        ("dez", 10),
        ("nove", 9),
        ("oito", 8),
        ("sete", 7),
        ("seis", 6),
        ("cinco", 5),
        ("quatro", 4),
        ("tres", 3),
        ("dois", 2),
        ("um", 1),
    ];
}
