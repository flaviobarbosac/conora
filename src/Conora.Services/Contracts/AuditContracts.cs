namespace Conora.Services.Contracts;

public sealed record AuditEventResponse(
    Guid Id,
    string EntityName,
    string EntityId,
    string Action,
    string Actor,
    DateTime TimestampUtc,
    string CorrelationId,
    string DetailsJson);

public sealed record AuditEventFilter(string? EntityName, string? EntityId, int Skip, int Take);
