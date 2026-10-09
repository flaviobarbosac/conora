using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record LifeProjectRequest(
    string Name,
    decimal GoalAmount,
    DateTime DueDate,
    string ContributionStartYm,
    LifeProjectScope Scope = LifeProjectScope.Personal,
    Guid? ChartAccountId = null,
    string? DetailedDescription = null);

public sealed record ProjectContributionRequest(
    decimal Amount,
    DateTime OccurredAt,
    Guid? AccountId = null,
    Guid? ChartAccountId = null,
    string? Description = null);

public sealed record LifeProjectResponse(
    Guid Id,
    string Name,
    string? DetailedDescription,
    decimal GoalAmount,
    DateTime DueDate,
    string ContributionStartYm,
    decimal AccumulatedAmount,
    decimal ProgressPercent,
    LifeProjectScope Scope,
    bool IsOwner,
    Guid? ChartAccountId,
    string? ChartAccountName,
    string? Horizon);
