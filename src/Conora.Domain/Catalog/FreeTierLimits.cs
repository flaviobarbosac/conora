namespace Conora.Domain.Catalog;

/// <summary>
/// Limits for the free (non-student / post-trial) product. Full access stays on student trial and paid plans.
/// </summary>
public static class FreeTierLimits
{
    public const int MaxBankAccounts = 1;

    /// <summary>Analytical expense categories the free user may budget and launch against.</summary>
    public static readonly HashSet<string> AllowedCategoryCodes = new(StringComparer.Ordinal)
    {
        "ESS_RENT",
        "ESS_POWER",
        "ESS_WATER",
        "ESS_SCHOOL"
    };

    public static bool AllowsCategoryCode(string? code)
        => !string.IsNullOrWhiteSpace(code) && AllowedCategoryCodes.Contains(code.Trim().ToUpperInvariant());
}
