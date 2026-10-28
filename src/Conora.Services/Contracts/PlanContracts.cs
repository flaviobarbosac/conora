using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

/// <summary>
/// Development / Testing only: activates a plan with a local stub token. Production plans come
/// from <see cref="StoreConfirmRequest"/> (validated with the store) and store notifications.
/// </summary>
public sealed record SubscribeRequest(PlanKind Plan, string? StorePurchaseToken = null);

/// <summary>
/// Sent by the app after the native purchase sheet. Channel: 1 = Google Play, 2 = App Store.
/// TransactionId is the Apple transaction id (or Google order id); PurchaseToken is the Google token.
/// </summary>
public sealed record StoreConfirmRequest(int Channel, string ProductId, string TransactionId, string? PurchaseToken);

/// <summary>Store-confirmed subscription state to mirror on the user's workspace subscription.</summary>
public sealed record StorePurchaseConfirmation(
    Guid UsuarioId,
    PlanKind Plan,
    string Store,
    string ProductId,
    string StoreSubscriptionId,
    DateTime? ExpiresAt,
    bool Entitled);

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
    IReadOnlyList<PlanOfferResponse> Offers,
    bool IsTrainingStudent,
    bool HasFullAccess,
    bool EverSubscribed);

