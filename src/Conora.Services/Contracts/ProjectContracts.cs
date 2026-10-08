using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record LifeProjectRequest(
    string Name,
    decimal GoalAmount,
    DateTime? DueDate = null,
    LifeProjectScope Scope = LifeProjectScope.Personal,
    Guid? ChartAccountId = null);

public sealed record ProjectContributionRequest(decimal Amount, DateTime OccurredAt, Guid? AccountId = null, string? Description = null);

public sealed record LifeProjectResponse(
    Guid Id,
    string Name,
    decimal GoalAmount,
    DateTime? DueDate,
    decimal AccumulatedAmount,
    decimal ProgressPercent,
    LifeProjectScope Scope,
    bool IsOwner,
    Guid? ChartAccountId,
    string? ChartAccountName);
