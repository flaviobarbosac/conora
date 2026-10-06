namespace Conora.Services.Contracts;

public sealed record ReopenMonthRequest(string Reason);

public sealed record MonthStatusResponse(
    string CompetenceYm, bool IsClosed, DateTime? ClosedAt, string? ReopenReason, Guid? ClosedByUsuarioId);
