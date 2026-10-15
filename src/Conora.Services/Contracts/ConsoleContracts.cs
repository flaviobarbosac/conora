namespace Conora.Services.Contracts;

public sealed record ConsoleKpisResponse(
    DateTime GeneratedAtUtc,
    string CompetenceYm,
    string PreviousCompetenceYm,
    int Accounts,
    int Active1d,
    int Active7d,
    int Active30d,
    int Errors7d,
    int Errors30d,
    int WithBudget,
    int WithoutBudget,
    int BudgetWithin,
    int BudgetOver,
    int BudgetEmpty,
    IReadOnlyList<ConsoleMetricResponse> Journey,
    IReadOnlyList<ConsoleMetricResponse> Achievements,
    IReadOnlyList<ConsoleMetricResponse> PreviousMonth,
    IReadOnlyList<ConsoleFeatureResponse> Features,
    IReadOnlyList<ConsoleWeekResponse> Weeks,
    IReadOnlyList<ConsoleMetricResponse> ErrorDays,
    IReadOnlyList<ConsoleMetricResponse> ErrorsByStatus,
    IReadOnlyList<ConsoleMetricResponse> ErrorsByArea,
    IReadOnlyList<ConsoleErrorRowResponse> ErrorRows);

public sealed record ConsoleMetricResponse(string Key, string Label, int Count, int Base, decimal Percent);

public sealed record ConsoleFeatureResponse(string Key, string Label, int Users, int Events, decimal Percent);

public sealed record ConsoleWeekResponse(string WeekStart, int Signups, int Active, int Budgets, int Closes, int Errors);

public sealed record ConsoleErrorRowResponse(string Day, int StatusCode, string Title, string Area, int Count);
