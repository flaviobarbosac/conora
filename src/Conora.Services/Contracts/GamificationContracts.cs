namespace Conora.Services.Contracts;

public sealed record JourneyStepResponse(
    string Key,
    string Title,
    string Route,
    bool Completed,
    bool IsNext);

public sealed record JourneyResponse(
    bool QuietMode,
    IReadOnlyList<JourneyStepResponse> Steps,
    JourneyStepResponse? NextStep);

public sealed record AchievementResponse(
    string Key,
    string Title,
    string Description,
    bool Earned,
    DateTime? EarnedAt);

public sealed record AchievementsResponse(
    bool QuietMode,
    IReadOnlyList<AchievementResponse> Items);

public sealed record ReserveThermometerResponse(
    string ReferenceYm,
    decimal MonthlyEssentialAverage,
    int MonthsConsidered,
    string Source,
    decimal ReserveBalance,
    decimal MonthsCovered,
    int TargetMonths,
    decimal ProgressPercent);

public sealed record ProjectProgressResponse(
    Guid Id,
    string Name,
    string Scope,
    decimal GoalAmount,
    decimal AccumulatedAmount,
    decimal ProgressPercent,
    DateTime? EstimatedArrival,
    string? Horizon);

public sealed record BudgetCloseStatusResponse(
    string CompetenceYm,
    bool IsClosed,
    bool HasBudget,
    decimal PlannedExpense,
    decimal ActualExpense,
    bool WithinBudget);

public sealed record HomeProgressResponse(
    bool QuietMode,
    JourneyStepResponse? NextStep,
    ReserveThermometerResponse Reserve,
    BudgetCloseStatusResponse BudgetClose,
    IReadOnlyList<ProjectProgressResponse> Projects,
    IReadOnlyList<AchievementResponse> Achievements);

public sealed record MonthCloseSummaryResponse(
    string CompetenceYm,
    bool IsClosed,
    DateTime? ClosedAt,
    string? ReopenReason,
    Guid? ClosedByUsuarioId,
    decimal IncomeTotal,
    decimal ExpenseTotal,
    decimal Result,
    bool WithinBudget,
    bool QuietMode,
    string? CelebrationMessage,
    JourneyStepResponse? NextStep);

public sealed record YearRetrospectiveMonth(
    string CompetenceYm,
    bool IsClosed,
    bool WithinBudget,
    decimal? IncomeTotal,
    decimal? ExpenseTotal,
    decimal? Result);

public sealed record YearRetrospectiveResponse(
    int Year,
    bool QuietMode,
    bool IncludeAbsoluteValues,
    int ClosedMonths,
    int WithinBudgetMonths,
    IReadOnlyList<YearRetrospectiveMonth> Months,
    IReadOnlyList<AchievementResponse> Achievements);
