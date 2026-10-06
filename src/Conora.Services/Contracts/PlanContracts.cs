using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record SubscribeRequest(PlanKind Plan);

/// <summary>Plan is null while the workspace has no subscription yet (trial: writable).</summary>
public sealed record PlanResponse(PlanKind? Plan, SubscriptionStatus Status, DateTime? ExpiresAt, bool IsReadOnly, decimal? Price);
