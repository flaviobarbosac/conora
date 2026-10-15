namespace Conora.Domain.Enums;

/// <summary>What the workspace may do right now, derived from trial age and subscription.</summary>
public enum AccessPhase
{
    /// <summary>Free full access during the first 90 days.</summary>
    Trial = 0,
    /// <summary>Active paid plan (own, or the family group owner's family plan).</summary>
    Subscribed = 1,
    /// <summary>Days 90 to 100 without a plan: read-only, export allowed, plans shown on every entry.</summary>
    Grace = 2,
    /// <summary>Paid plan expired: read-only until renewed.</summary>
    Expired = 3,
    /// <summary>Past day 100 without a plan: only the plans screen; data only via support.</summary>
    Blocked = 4
}
