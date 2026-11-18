using Conora.Domain.Entities;
using Conora.Domain.Enums;

namespace Conora.Domain.Services;

public sealed record AccessState(AccessPhase Phase, DateTime TrialEndsAt, DateTime BlockAt)
{
    /// <summary>
    /// Trial, a console grant and a paid plan write the full product.
    /// A lapsed paid plan (expired) still writes the free tier. Blocked is the plans screen only.
    /// </summary>
    public bool IsWritable => Phase is AccessPhase.Trial or AccessPhase.Granted or AccessPhase.Subscribed or AccessPhase.Grace or AccessPhase.Expired;
    public bool IsBlocked => Phase == AccessPhase.Blocked;
}

/// <summary>
/// Access lifecycle without a paid plan: 7 days of full access (Duo and Família invites stay out),
/// then the plans screen. Training students (Hotmart purchase of Leomar Campos's course) keep 90 days.
/// A console operator can move the tasting end date, or grant access with no end or until a date.
/// </summary>
public static class AccessPolicy
{
    public const int StudentTrialDays = 90;
    public const int StandardTrialDays = 7;
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
    /// <param name="isTrainingStudent">When true, 90-day trial; otherwise 7. Default true preserves current cohort.</param>
    /// <param name="trialEndsAt">Console or course override of the tasting end. Null uses the profile length.</param>
    /// <param name="complimentaryIndefinite">Console grant with no end date.</param>
    /// <param name="complimentaryUntil">Console grant that ends at this instant.</param>
    public static AccessState Evaluate(
        DateTime accountCreatedAt,
        WorkspaceSubscription? subscription,
        DateTime now,
        bool isTrainingStudent = true,
        DateTime? trialEndsAt = null,
        bool complimentaryIndefinite = false,
        DateTime? complimentaryUntil = null)
    {
        var start = TrialStart(accountCreatedAt);
        var trialDays = TrialDaysFor(isTrainingStudent);
        var trialEnds = trialEndsAt ?? start.AddDays(trialDays);
        var blockAt = trialEnds.AddDays(GraceDaysAfterTrial);

        if (subscription is not null && subscription.IsWritable(now))
            return new AccessState(AccessPhase.Subscribed, trialEnds, blockAt);

        if (complimentaryIndefinite || (complimentaryUntil is DateTime until && now < until))
            return new AccessState(AccessPhase.Granted, complimentaryUntil ?? trialEnds, blockAt);

        if (subscription is not null)
            return new AccessState(AccessPhase.Expired, trialEnds, blockAt);

        if (now < trialEnds)
            return new AccessState(AccessPhase.Trial, trialEnds, blockAt);

        return new AccessState(AccessPhase.Blocked, trialEnds, blockAt);
    }

    /// <summary>Instant when the chosen Brazil calendar day ends (start of the next day, UTC).</summary>
    public static DateTime BrazilDayEndsUtc(DateOnly day)
    {
        var tzId = OperatingSystem.IsWindows()
            ? "E. South America Standard Time"
            : "America/Sao_Paulo";
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        var nextMidnight = new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Unspecified).AddDays(1);
        return TimeZoneInfo.ConvertTimeToUtc(nextMidnight, tz);
    }

    /// <summary>Whole days left until <paramref name="until"/> (rounded up, never negative).</summary>
    public static int DaysUntil(DateTime until, DateTime now)
        => Math.Max(0, (int)Math.Ceiling((until - now).TotalDays));

    /// <summary>True while the tasting is still open and ends within the next 24 hours.</summary>
    public static bool IsInsideEndingDay(DateTime now, DateTime trialEnds)
        => trialEnds > now && trialEnds - now <= TimeSpan.FromHours(24);
}
