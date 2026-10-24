using Conora.Domain.Enums;

namespace Conora.Domain.Catalog;

public sealed record PlanOfferDefinition(PlanKind Kind, PlanTier Tier, bool Yearly, decimal Price, decimal MonthlyEquivalent);

/// <summary>
/// Sellable plans. Prices follow the App Store price points (BRL); yearly is the store tier
/// just above 10 monthly fees (close to 2 months free).
/// </summary>
public static class PlanCatalog
{

    /// <summary>Extra people on Duo (PlanTier.Family): owner + 1 = 2 people total.</summary>
    public const int FamilyExtraMembers = 1;

    /// <summary>Extra people on Family (PlanTier.FamilyPlus): owner + 5 = 6 people total.</summary>
    public const int FamilyPlusExtraMembers = 5;

    public static readonly IReadOnlyList<PlanOfferDefinition> Offers =
    [
        Offer(PlanKind.IndividualMonthly, PlanTier.Individual, 49.90m, yearly: false),
        Offer(PlanKind.IndividualYearly, PlanTier.Individual, 499.90m, yearly: true),
        Offer(PlanKind.FamilyMonthly, PlanTier.Family, 59.90m, yearly: false),
        Offer(PlanKind.FamilyYearly, PlanTier.Family, 599.90m, yearly: true),
        Offer(PlanKind.FamilyPlusMonthly, PlanTier.FamilyPlus, 79.90m, yearly: false),
        Offer(PlanKind.FamilyPlusYearly, PlanTier.FamilyPlus, 799.90m, yearly: true)
    ];

    public static bool IsSellable(PlanKind kind) => Offers.Any(o => o.Kind == kind);

    public static PlanTier TierOf(PlanKind kind) => kind switch
    {
        PlanKind.FamilyMonthly or PlanKind.FamilyYearly => PlanTier.Family,
        PlanKind.FamilyPlusMonthly or PlanKind.FamilyPlusYearly => PlanTier.FamilyPlus,
        _ => PlanTier.Individual
    };

    public static bool IsYearly(PlanKind kind) => kind is PlanKind.Yearly14990
        or PlanKind.IndividualYearly or PlanKind.FamilyYearly or PlanKind.FamilyPlusYearly;

    public static decimal PriceOf(PlanKind kind) => kind switch
    {
        PlanKind.Monthly1490 => 14.90m,
        PlanKind.Yearly14990 => 149.90m,
        _ => Offers.FirstOrDefault(o => o.Kind == kind)?.Price
             ?? throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>Extra family members allowed by the tier (beyond the owner).</summary>
    public static int? MaxExtraMembers(PlanTier tier) => tier switch
    {
        PlanTier.Individual => 0,
        PlanTier.Family => FamilyExtraMembers,
        PlanTier.FamilyPlus => FamilyPlusExtraMembers,
        _ => null
    };

    /// <summary>Shown when an invite would pass the tier seat limit.</summary>
    public static string FamilySeatMessage(PlanTier tier) => tier switch
    {
        PlanTier.Family =>
            "O plano Duo permite só você e mais 1 pessoa (2 no total). Para incluir mais gente, mude para o plano Família.",
        PlanTier.FamilyPlus =>
            "O plano Família permite você e mais 5 pessoas (6 no total).",
        _ =>
            "O plano Individual é só para você. Para convidar alguém, mude para o Duo (você + 1) ou para o Família (você + 5)."
    };

    private static PlanOfferDefinition Offer(PlanKind kind, PlanTier tier, decimal price, bool yearly) =>
        yearly
            ? new PlanOfferDefinition(kind, tier, true, price, Math.Round(price / 12, 2))
            : new PlanOfferDefinition(kind, tier, false, price, price);
}
