using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record PatrimonyItemRequest(PatrimonyKind Kind, string Name, decimal Amount);

public sealed record UpdatePatrimonyItemRequest(string Name, decimal Amount);

public sealed record PatrimonyItemResponse(Guid Id, PatrimonyKind Kind, string Name, decimal Amount);

public sealed record PatrimonySummaryResponse(
    decimal AccountsBalance,
    decimal AssetsTotal,
    decimal UnpaidCardInvoices,
    decimal LiabilitiesTotal,
    decimal NetWorth,
    IReadOnlyList<PatrimonyItemResponse> Items);

/// <summary>Reserve base = average monthly essential spending. Source: Last3Months | AvailableMonths | Budget | None.</summary>
public sealed record ReserveResponse(string ReferenceYm, decimal MonthlyEssentialAverage, int MonthsConsidered, string Source);
