namespace Conora.Services.Contracts;

public sealed record CloseMonthRequest(IReadOnlyList<Guid>? CategoryIds = null);

public sealed record CloseGapResponse(
    Guid CategoryId,
    string CategoryName,
    decimal PlannedAmount,
    decimal ActualAmount,
    decimal OpenAmount,
    bool AlreadyRegistered);

public sealed record PendingDebtResponse(
    Guid Id,
    string CompetenceYm,
    Guid CategoryId,
    string CategoryName,
    decimal OriginalAmount,
    decimal OpenAmount,
    DateTime? PaidAt);
