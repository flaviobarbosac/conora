using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record BudgetLineInput(Guid CategoryId, decimal PlannedAmount);

public sealed record UpsertBudgetRequest(BudgetMode Mode, IReadOnlyList<BudgetLineInput> Lines);

/// <summary>Status follows the fixed rule: under 70% Ok, 70-99% Attention, 100% Limit, above 100% Exceeded.</summary>
public sealed record BudgetLineResponse(
    Guid? CategoryId,
    string CategoryName,
    string GroupName,
    BudgetBlock Block,
    decimal PlannedAmount,
    decimal ActualAmount,
    decimal Remaining,
    decimal? Percent,
    string Status,
    bool IsGroup);

public sealed record BudgetIncomeSourceResponse(Guid Id, string Name, decimal NetSpendable);

public sealed record BudgetBlockResponse(
    BudgetBlock Block,
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
    decimal MonthResult,
    IReadOnlyList<BudgetIncomeSourceResponse> IncomeSources,
    IReadOnlyList<BudgetBlockResponse> Blocks,
    IReadOnlyList<BudgetLineResponse> Lines);

public sealed record BudgetYearMonthCell(string CompetenceYm, decimal PlannedAmount, decimal ActualAmount);

public sealed record BudgetYearLineResponse(
    Guid? CategoryId,
    string CategoryName,
    string GroupName,
    BudgetBlock Block,
    IReadOnlyList<BudgetYearMonthCell> Months);

public sealed record BudgetYearResponse(
    int Year,
    IReadOnlyList<string> Months,
    IReadOnlyList<BudgetYearLineResponse> Lines,
    IReadOnlyList<BudgetYearMonthCell> Totals);
