using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record BudgetLineInput(Guid CategoryId, decimal PlannedAmount);

public sealed record UpsertBudgetRequest(BudgetMode Mode, IReadOnlyList<BudgetLineInput> Lines);

/// <summary>Status follows the fixed rule: under 70% Ok, 70-99% Attention, 100% Limit, above 100% Exceeded.</summary>
public sealed record BudgetLineResponse(
    Guid CategoryId,
    string CategoryName,
    decimal PlannedAmount,
    decimal ActualAmount,
    decimal Remaining,
    decimal? Percent,
    string Status);

public sealed record BudgetResponse(
    string CompetenceYm,
    BudgetMode Mode,
    decimal TotalPlanned,
    decimal TotalActual,
    decimal ProjectedExpense,
    IReadOnlyList<BudgetLineResponse> Lines);
