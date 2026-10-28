using Conora.Domain.Entities;
using Conora.Domain.Enums;

namespace Conora.Domain.Services;

public sealed record AccessState(AccessPhase Phase, DateTime TrialEndsAt, DateTime BlockAt)
{
    /// <summary>
    /// Free tier (grace / expired) still writes limited data; blocked is plans-only.
    /// </summary>
    public bool IsWritable => Phase is AccessPhase.Trial or AccessPhase.Subscribed or AccessPhase.Grace or AccessPhase.Expired;
    public bool IsBlocked => Phase == AccessPhase.Blocked;
}

/// <summary>
/// Access lifecycle without a paid plan: free full access (trial), then read-only grace, then blocked.
/// Trial length is profile-based (not a store product): 30 days for everyone;
/// training students (Hotmart purchase of Leomar Campos's course) get 30 + 60 = 90 days.
/// </summary>
public static class AccessPolicy
{
    public const int StudentTrialDays = 90;
    public const int StandardTrialDays = 30;
    public const int GraceDaysAfterTrial = 10;

    /// <summary>Alias for student trial (legacy callers / docs that still say TrialDays = 90).</summary>
    public const int TrialDays = StudentTrialDays;

    public const int ReadOnlyUntilDay = StudentTrialDays + GraceDaysAfterTrial;

    /// <summary>
    /// Accounts created before the plans went live start their trial on this date,
    /// so nobody is blocked retroactively.
    /// </summary>
    public static readonly DateTime TrialFloorUtc = new(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);

    public static int TrialDaysFor(bool isTrainingStudent) =>
        isTrainingStudent ? StudentTrialDays : StandardTrialDays;

    public static DateTime TrialStart(DateTime accountCreatedAt)
        => accountCreatedAt > TrialFloorUtc ? accountCreatedAt : TrialFloorUtc;

    /// <param name="subscription">The plan covering this workspace (own or family owner's), if any.</param>
    /// <param name="isTrainingStudent">When true, 90-day trial; otherwise 30. Default true preserves current cohort.</param>
    public static AccessState Evaluate(
        DateTime accountCreatedAt,
        WorkspaceSubscription? subscription,
        DateTime now,
        bool isTrainingStudent = true)
    {
        var start = TrialStart(accountCreatedAt);
        var trialDays = TrialDaysFor(isTrainingStudent);
        var trialEnds = start.AddDays(trialDays);
        var blockAt = start.AddDays(trialDays + GraceDaysAfterTrial);

        if (subscription is not null)
        {
            var phase = subscription.IsWritable(now) ? AccessPhase.Subscribed : AccessPhase.Expired;
            return new AccessState(phase, trialEnds, blockAt);
        }

        if (now < trialEnds)
            return new AccessState(AccessPhase.Trial, trialEnds, blockAt);
        // After trial without a plan the product stays on free-tier limits (not app-wide read-only / blocked).
        return new AccessState(AccessPhase.Expired, trialEnds, blockAt);
    }

    /// <summary>Whole days left until <paramref name="until"/> (rounded up, never negative).</summary>
    public static int DaysUntil(DateTime until, DateTime now)
        => Math.Max(0, (int)Math.Ceiling((until - now).TotalDays));
}
