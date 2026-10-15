namespace Conora.Domain.Enums;

/// <summary>
/// Plan tier + billing cycle. Persisted as string. Monthly1490/Yearly14990 are legacy
/// values kept so existing rows still load; they behave as the Individual tier.
/// </summary>
public enum PlanKind
{
    Monthly1490 = 0,
    Yearly14990 = 1,
    IndividualMonthly = 2,
    IndividualYearly = 3,
    FamilyMonthly = 4,
    FamilyYearly = 5,
    FamilyPlusMonthly = 6,
    FamilyPlusYearly = 7
}
