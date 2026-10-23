namespace Conora.Domain.Enums;

public enum PlanTier
{
    /// <summary>All features except the family group.</summary>
    Individual = 0,
    /// <summary>Sold as Duo: owner plus one other person (2 total).</summary>
    Family = 1,
    /// <summary>Sold as Family: owner plus 3 or more people.</summary>
    FamilyPlus = 2
}
