using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record PatrimonyItemRequest(Guid ChartAccountId, decimal Amount);

public sealed record UpdatePatrimonyItemRequest(decimal Amount);

public sealed record PatrimonyItemResponse(
    Guid Id,
    Guid ChartAccountId,
    string ChartAccountName,
    ChartSection Section,
    string GroupName,
    decimal Amount);

public sealed record PatrimonyGroupTotal(string GroupName, ChartSection Section, decimal Amount);

public sealed record PatrimonySummaryResponse(
    decimal AccountsBalance,
    decimal AssetsTotal,
    decimal AssetsInUse,
    decimal AssetsNotInUse,
    decimal UnpaidCardInvoices,
    decimal LiabilitiesTotal,
    decimal NetWorth,
    IReadOnlyList<PatrimonyGroupTotal> Groups,
    IReadOnlyList<PatrimonyItemResponse> Items);

/// <summary>Reserve base = average monthly essential spending. Source: Last3Months | AvailableMonths | Budget | None.</summary>
public sealed record ReserveResponse(string ReferenceYm, decimal MonthlyEssentialAverage, int MonthsConsidered, string Source);
