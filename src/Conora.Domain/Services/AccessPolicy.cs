using Conora.Domain.Entities;
using Conora.Domain.Enums;

namespace Conora.Domain.Services;

public sealed record AccessState(AccessPhase Phase, DateTime TrialEndsAt, DateTime BlockAt)
{
    public bool IsWritable => Phase is AccessPhase.Trial or AccessPhase.Subscribed;
    public bool IsBlocked => Phase == AccessPhase.Blocked;
}

/// <summary>
/// Access lifecycle: 90 days of free full access (student trial), then read-only with export
/// until day 100, then blocked (only the plans screen; data via support).
/// </summary>
public static class AccessPolicy
{
    public const int TrialDays = 90;
    public const int ReadOnlyUntilDay = 100;

    /// <summary>
    /// Accounts created before the plans went live start their trial on this date,
    /// so nobody is blocked retroactively.
    /// </summary>
    public static readonly DateTime TrialFloorUtc = new(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);

    public static DateTime TrialStart(DateTime accountCreatedAt)
        => accountCreatedAt > TrialFloorUtc ? accountCreatedAt : TrialFloorUtc;

    /// <param name="subscription">The plan covering this workspace (own or family owner's), if any.</param>
    public static AccessState Evaluate(DateTime accountCreatedAt, WorkspaceSubscription? subscription, DateTime now)
    {
        var start = TrialStart(accountCreatedAt);
        var trialEnds = start.AddDays(TrialDays);
        var blockAt = start.AddDays(ReadOnlyUntilDay);

        if (subscription is not null)
        {
            var phase = subscription.IsWritable(now) ? AccessPhase.Subscribed : AccessPhase.Expired;
            return new AccessState(phase, trialEnds, blockAt);
        }

        if (now < trialEnds)
            return new AccessState(AccessPhase.Trial, trialEnds, blockAt);
        if (now < blockAt)
            return new AccessState(AccessPhase.Grace, trialEnds, blockAt);
        return new AccessState(AccessPhase.Blocked, trialEnds, blockAt);
    }

    /// <summary>Whole days left until <paramref name="until"/> (rounded up, never negative).</summary>
    public static int DaysUntil(DateTime until, DateTime now)
        => Math.Max(0, (int)Math.Ceiling((until - now).TotalDays));
}
