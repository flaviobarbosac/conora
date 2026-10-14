namespace Conora.Domain.Catalog;

/// <summary>Theme ids by climate. Free themes for everyone; extras require yearly plan.</summary>
public static class ThemeCatalog
{
    public const string Clareza = "clareza";
    public const string Noite = "noite";
    public const string Contraste = "contraste";
    public const string Salvia = "salvia";
    public const string Lavanda = "lavanda";
    public const string Terracota = "terracota";

    public static readonly IReadOnlyList<string> Free = [Clareza, Noite, Contraste];
    public static readonly IReadOnlyList<string> Annual = [Salvia, Lavanda, Terracota];
    public static readonly IReadOnlyList<string> All = [Clareza, Noite, Contraste, Salvia, Lavanda, Terracota];

    public static bool IsKnown(string themeId)
        => All.Contains(themeId, StringComparer.OrdinalIgnoreCase);

    public static bool RequiresAnnualPlan(string themeId)
        => Annual.Contains(themeId, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
            return Clareza;
        var id = themeId.Trim().ToLowerInvariant();
        return IsKnown(id) ? id : Clareza;
    }
}
