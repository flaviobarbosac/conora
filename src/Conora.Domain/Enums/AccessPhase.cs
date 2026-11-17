namespace Conora.Domain.Enums;

/// <summary>What the workspace may do right now, derived from trial age and subscription.</summary>
public enum AccessPhase
{
    /// <summary>Tasting: 7 days for everyone, 90 for a training student. Full product except Duo and Família invites.</summary>
    Trial = 0,
    /// <summary>Active paid plan (own, or the family group owner's family plan).</summary>
    Subscribed = 1,
    /// <summary>Days 90 to 100 without a plan: read-only, export allowed, plans shown on every entry.</summary>
    Grace = 2,
    /// <summary>Paid plan expired: read-only until renewed.</summary>
    Expired = 3,
    /// <summary>Past the tasting without a plan: only the plans screen.</summary>
    Blocked = 4,
    /// <summary>Console grant: full product until a date, or with no end. Duo and Família invites stay out.</summary>
    Granted = 5
}
