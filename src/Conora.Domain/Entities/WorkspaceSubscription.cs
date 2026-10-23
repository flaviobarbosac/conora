using Conora.Domain.Catalog;
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

    public static decimal PriceOf(PlanKind plan) => PlanCatalog.PriceOf(plan);

    public PlanTier Tier => PlanCatalog.TierOf(Plan);

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

    /// <summary>"google-play" or "app-store" when the plan is billed by a store; null for legacy / dev plans.</summary>
    public string? Store { get; private set; }
    public string? StoreProductId { get; private set; }

    /// <summary>Stable store key across renewals: Apple originalTransactionId or Google purchase token.</summary>
    public string? StoreSubscriptionId { get; private set; }

    public static WorkspaceSubscription FromStore(
        PlanKind plan, DateTime expiresAt, string store, string productId, string storeSubscriptionId)
    {
        var sub = new WorkspaceSubscription();
        sub.ApplyStorePeriod(plan, expiresAt, store, productId, storeSubscriptionId);
        return sub;
    }

    /// <summary>
    /// Mirrors a store-confirmed active period. The store expiry is authoritative; a stale (older)
    /// notification for the same store subscription never shortens a later expiry.
    /// </summary>
    public void ApplyStorePeriod(PlanKind plan, DateTime expiresAt, string store, string productId, string storeSubscriptionId)
    {
        var sameSubscription = string.Equals(StoreSubscriptionId, storeSubscriptionId, StringComparison.Ordinal);
        if (sameSubscription && Status == SubscriptionStatus.Active && expiresAt < ExpiresAt)
            return;

        Plan = plan;
        Status = SubscriptionStatus.Active;
        ExpiresAt = expiresAt;
        Store = store;
        StoreProductId = productId;
        StoreSubscriptionId = storeSubscriptionId;
    }

    /// <summary>The store ended the subscription (expired, refunded or revoked).</summary>
    public void EndFromStore(DateTime endedAt)
    {
        Status = SubscriptionStatus.Expired;
        if (endedAt < ExpiresAt)
            ExpiresAt = endedAt;
    }

    public void MarkReadOnly() => Status = SubscriptionStatus.ReadOnly;

    /// <summary>Effective status: an Active subscription past its expiry is Expired.</summary>
    public SubscriptionStatus EffectiveStatus(DateTime now)
        => Status == SubscriptionStatus.Active && ExpiresAt <= now ? SubscriptionStatus.Expired : Status;

    public bool IsWritable(DateTime now) => EffectiveStatus(now) == SubscriptionStatus.Active;

    private static DateTime ExpiryFrom(PlanKind plan, DateTime from)
        => PlanCatalog.IsYearly(plan) ? from.AddYears(1) : from.AddMonths(1);
}
