namespace Conora.Domain.Catalog;

/// <summary>
/// Limits for the free (non-student / post-trial) product. Full access stays on student trial and paid plans.
/// </summary>
public static class FreeTierLimits
{
    public const int MaxBankAccounts = 1;

    /// <summary>Analytical categories the free user may budget and launch against (essentials + salary).</summary>
    public static readonly HashSet<string> AllowedCategoryCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ESS_RENT",
        "ESS_POWER",
        "ESS_WATER",
        "ESS_SCHOOL",
        "INC_SALARY",
    };

    public static bool AllowsCategoryCode(string? code)
        => !string.IsNullOrWhiteSpace(code) && AllowedCategoryCodes.Contains(code.Trim().ToUpperInvariant());
}
