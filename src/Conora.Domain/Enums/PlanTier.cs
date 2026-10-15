namespace Conora.Domain.Enums;

public enum PlanTier
{
    /// <summary>All features except the family group.</summary>
    Individual = 0,
    /// <summary>Family group with the owner plus up to 2 people.</summary>
    Family = 1,
    /// <summary>Family group with the owner plus 3 or more people.</summary>
    FamilyPlus = 2
}
