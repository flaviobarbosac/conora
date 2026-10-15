namespace Conora.Repository;

public sealed record ConsoleReadModel(
    DateTime GeneratedAtUtc,
    string CompetenceYm,
    string PreviousCompetenceYm,
    int Accounts,
    int OnboardingCompleted,
    int WithDiagnosis,
    int WithBudgetEver,
    int WithCard,
    int WithCloseEver,
    int WithReserve,
    int ReserveComplete,
    int WithProject,
    int ProjectAdvanced,
    int WithinEver,
    int ThreeMonthStreak,
    int WithBudgetThisMonth,
    int BudgetWithin,
    int BudgetOver,
    int BudgetEmpty,
    int WithEntryPrevious,
    int ClosedPrevious,
    int WithinPrevious,
    int Active1d,
    int Active7d,
    int Active30d,
    int Errors7d,
    int Errors30d,
    IReadOnlyList<ConsoleFeatureRaw> Features,
    IReadOnlyList<ConsoleWeekRaw> Weeks,
    IReadOnlyList<ConsoleDayRaw> ErrorDays,
    IReadOnlyList<ConsoleStatusRaw> ErrorsByStatus,
    IReadOnlyList<ConsoleAreaRaw> ErrorsByArea,
    IReadOnlyList<ConsoleErrorRowRaw> ErrorRows,
    IReadOnlyList<ConsoleMonthRaw> Months);

public sealed record ConsoleMonthRaw(
    string CompetenceYm,
    int Base,
    int WithBudget,
    int BudgetWithin,
    int BudgetOver,
    int BudgetEmpty,
    int WithEntry,
    int Closed,
    int ClosedWithin,
    int Signups,
    int Errors);

public sealed record ConsoleFeatureRaw(string Key, int Users, int Events);
public sealed record ConsoleWeekRaw(DateOnly WeekStart, int Signups, int Active, int Budgets, int Closes, int Errors);
public sealed record ConsoleDayRaw(DateOnly Day, int Count);
public sealed record ConsoleStatusRaw(int StatusCode, string Title, int Count);
public sealed record ConsoleAreaRaw(string Area, int Count);
public sealed record ConsoleErrorRowRaw(DateOnly Day, int StatusCode, string Title, string Area, int Count);
