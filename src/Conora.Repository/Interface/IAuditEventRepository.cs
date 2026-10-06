using Conora.Domain.Entities;

namespace Conora.Repository.Interface;

public interface IAuditEventRepository : IBaseRepository<AuditEvent>
{
    Task<(IReadOnlyList<AuditEvent> Items, int Total)> QueryAsync(
        string? entityName, string? entityId, int skip, int take, CancellationToken ct = default);
}
