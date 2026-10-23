namespace Conora.Infrastructure.Billing;

/// <summary>
/// Play / App Store product ids and credentials — mirrored from Clampfy StoreBillingOptions.
/// One SKU set for plan tiers (Individual / Duo / Family). Student vs standard free-trial
/// length is NOT a separate store product — resolved in AccessPolicy from the user profile.
/// </summary>
public sealed class StoreBillingOptions
{
    public const string SectionName = "StoreBilling";

    public string PackageName { get; set; } = "br.com.conora.app";
    public string BundleId { get; set; } = "br.com.conora.app";

    public string IndividualMonthlyProductId { get; set; } = StoreProductIds.IndividualMonthly;
    public string IndividualYearlyProductId { get; set; } = StoreProductIds.IndividualYearly;
    public string DuoMonthlyProductId { get; set; } = StoreProductIds.DuoMonthly;
    public string DuoYearlyProductId { get; set; } = StoreProductIds.DuoYearly;
    public string FamilyMonthlyProductId { get; set; } = StoreProductIds.FamilyMonthly;
    public string FamilyYearlyProductId { get; set; } = StoreProductIds.FamilyYearly;

    /// <summary>Shared secret for Google RTDN push (?k=). Empty rejects the Google webhook when set.</summary>
    public string NotificationKey { get; set; } = "";

    public string AppleRootCertificateSha256 { get; set; } = "";
    public string GoogleServiceAccountJsonPath { get; set; } = "";
    public string AppleIssuerId { get; set; } = "";
    public string AppleKeyId { get; set; } = "";
    public string ApplePrivateKeyPath { get; set; } = "";
}

/// <summary>Default store SKUs. Prices live in PlanCatalog; ids must match Play / App Store listings.</summary>
public static class StoreProductIds
{
    public const string IndividualMonthly = "conora.individual.monthly";
    public const string IndividualYearly = "conora.individual.yearly";
    public const string DuoMonthly = "conora.duo.monthly";
    public const string DuoYearly = "conora.duo.yearly";
    public const string FamilyMonthly = "conora.family.monthly";
    public const string FamilyYearly = "conora.family.yearly";
}
