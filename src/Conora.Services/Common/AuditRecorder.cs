using System.Text.Json;
using Conora.Domain.Entities;
using Conora.Domain.Ports;
using Conora.Repository.Interface;

namespace Conora.Services.Common;

public static class AuditRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Record(
        IAuditEventRepository audits,
        ICorrelationContext correlation,
        string entityName,
        Guid entityId,
        string action,
        object details)
    {
        var detailsJson = JsonSerializer.Serialize(details, JsonOptions);

        audits.Add(AuditEvent.Create(
            entityName,
            entityId.ToString(),
            action,
            correlation.Actor,
            correlation.CorrelationId,
            detailsJson));
    }
}
