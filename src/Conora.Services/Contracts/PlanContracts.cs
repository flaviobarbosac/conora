using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

/// <summary>
/// StorePurchaseToken is required: the plan only activates after Google Play / App Store confirmation
/// (or a local stub token in development).
/// </summary>
public sealed record SubscribeRequest(PlanKind Plan, string? StorePurchaseToken = null);

public sealed record StorePurchaseConfirmation(
    Guid UsuarioId,
    PlanKind Plan,
    string PurchaseToken,
    string Store);

public sealed record PlanOfferResponse(
    PlanKind Kind,
    PlanTier Tier,
    bool Yearly,
    decimal Price,
    decimal MonthlyEquivalent,
    int? MaxExtraMembers);

/// <summary>
/// Plan is null while the workspace has no subscription (trial). Phase drives the UI:
/// Trial/Subscribed = full access, Grace/Expired = read-only, Blocked = plans screen only.
/// </summary>
public sealed record PlanResponse(
    PlanKind? Plan,
    PlanTier? Tier,
    bool Yearly,
    SubscriptionStatus Status,
    DateTime? ExpiresAt,
    bool IsReadOnly,
    decimal? Price,
    AccessPhase Phase,
    DateTime TrialEndsAt,
    DateTime BlockAt,
    int DaysLeft,
    bool CoveredByFamily,
    IReadOnlyList<PlanOfferResponse> Offers);

public sealed record FeedbackRequest(string Tried, string Blocked);
