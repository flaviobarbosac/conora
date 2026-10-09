using Conora.Domain.Enums;

namespace Conora.Domain.Entities;

public class WorkspaceSubscription : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public PlanKind Plan { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    private WorkspaceSubscription()
    {
    }

    public static decimal PriceOf(PlanKind plan) => plan switch
    {
        PlanKind.Monthly1490 => 14.90m,
        PlanKind.Yearly14990 => 149.90m,
        _ => throw new ArgumentOutOfRangeException(nameof(plan))
    };

    public static WorkspaceSubscription Activate(PlanKind plan, DateTime now) => new()
    {
        Plan = plan,
        Status = SubscriptionStatus.Active,
        ExpiresAt = ExpiryFrom(plan, now)
    };

    /// <summary>Renews from the current expiry when still valid, otherwise from now.</summary>
    public void Renew(PlanKind plan, DateTime now)
    {
        var baseDate = ExpiresAt > now ? ExpiresAt : now;
        Plan = plan;
        Status = SubscriptionStatus.Active;
        ExpiresAt = ExpiryFrom(plan, baseDate);
    }

    public void MarkReadOnly() => Status = SubscriptionStatus.ReadOnly;

    /// <summary>Effective status: an Active subscription past its expiry is Expired.</summary>
    public SubscriptionStatus EffectiveStatus(DateTime now)
        => Status == SubscriptionStatus.Active && ExpiresAt <= now ? SubscriptionStatus.Expired : Status;

    public bool IsWritable(DateTime now) => EffectiveStatus(now) == SubscriptionStatus.Active;

    private static DateTime ExpiryFrom(PlanKind plan, DateTime from) => plan switch
    {
        PlanKind.Monthly1490 => from.AddMonths(1),
        PlanKind.Yearly14990 => from.AddYears(1),
        _ => throw new ArgumentOutOfRangeException(nameof(plan))
    };
}
