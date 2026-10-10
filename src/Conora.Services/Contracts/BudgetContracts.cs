using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record BudgetLineInput(Guid CategoryId, decimal PlannedAmount);

public sealed record UpsertBudgetRequest(BudgetMode Mode, IReadOnlyList<BudgetLineInput> Lines);

/// <summary>Status follows the fixed rule: under 70% Ok, 70-99% Attention, 100% Limit, above 100% Exceeded.</summary>
public sealed record BudgetLineResponse(
    Guid? CategoryId,
    string CategoryName,
    Guid? ParentId,
    string GroupName,
    CategorySection Section,
    CategoryLevel Level,
    decimal PlannedAmount,
    decimal ActualAmount,
    decimal Remaining,
    decimal? Percent,
    string Status,
    bool IsGroup);

public sealed record BudgetIncomeSourceResponse(Guid Id, string Name, decimal NetSpendable);

public sealed record BudgetSectionResponse(
    CategorySection Section,
    string Name,
    decimal PlannedAmount,
    decimal ActualAmount,
    decimal? PercentOfSpendable,
    IReadOnlyList<BudgetLineResponse> Lines);

public sealed record BudgetResponse(
    string CompetenceYm,
    BudgetMode Mode,
    decimal TotalPlanned,
    decimal TotalActual,
    decimal ProjectedExpense,
    decimal SpendableIncome,
    decimal ReceivedIncome,
    decimal MonthResult,
    IReadOnlyList<BudgetIncomeSourceResponse> IncomeSources,
    IReadOnlyList<BudgetSectionResponse> Sections,
    IReadOnlyList<BudgetLineResponse> Lines);

public sealed record BudgetYearMonthCell(string CompetenceYm, decimal PlannedAmount, decimal ActualAmount);

public sealed record BudgetYearLineResponse(
    Guid? CategoryId,
    string CategoryName,
    string GroupName,
    CategorySection Section,
    IReadOnlyList<BudgetYearMonthCell> Months);

public sealed record BudgetYearResponse(
    int Year,
    IReadOnlyList<string> Months,
    IReadOnlyList<BudgetYearLineResponse> Lines,
    IReadOnlyList<BudgetYearMonthCell> Totals);

/// <param name="Overwrite">When false, skip accounts that already have planned &gt; 0 in the target month.</param>
public sealed record CopyPreviousBudgetRequest(bool Overwrite = false);

/// <summary>Copies the planned amount into the next N-1 months (total N including start). Optional amount overrides the start month.</summary>
public sealed record RepeatBudgetRequest(
    Guid CategoryId,
    int MonthCount,
    bool Overwrite = false,
    decimal? PlannedAmount = null);

/// <summary>Splits <paramref name="TotalAmount"/> across <paramref name="InstallmentCount"/> months starting at the route competence.</summary>
public sealed record InstallmentBudgetRequest(
    Guid CategoryId,
    decimal TotalAmount,
    int InstallmentCount,
    bool Overwrite = false);
