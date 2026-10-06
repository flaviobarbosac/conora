namespace Conora.Domain.Entities;

public class AuditEvent : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string EntityName { get; private set; } = default!;
    public string EntityId { get; private set; } = default!;
    public string Action { get; private set; } = default!;
    public string Actor { get; private set; } = default!;
    public DateTime TimestampUtc { get; private set; }
    public string CorrelationId { get; private set; } = default!;
    public string DetailsJson { get; private set; } = default!;

    private AuditEvent()
    {
    }

    public static AuditEvent Create(
        string entityName,
        string entityId,
        string action,
        string actor,
        string correlationId,
        string detailsJson)
    {
        return new AuditEvent
        {
            EntityName = entityName,
            EntityId = entityId,
            Action = action,
            Actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor,
            TimestampUtc = DateTime.UtcNow,
            CorrelationId = correlationId,
            DetailsJson = detailsJson
        };
    }
}
