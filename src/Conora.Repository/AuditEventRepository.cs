using Conora.Domain.Entities;
using Conora.Domain.Ports;
using Conora.Infrastructure.Persistence;
using Conora.Repository.Interface;
using Microsoft.EntityFrameworkCore;

namespace Conora.Repository;

public sealed class AuditEventRepository : BaseRepository<AuditEvent>, IAuditEventRepository
{
    public AuditEventRepository(AppDbContext context, ITenantContext tenant) : base(context, tenant)
    {
    }

    public async Task<(IReadOnlyList<AuditEvent> Items, int Total)> QueryAsync(
        string? entityName, string? entityId, int skip, int take, CancellationToken ct = default)
    {
        var query = Set.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityName))
            query = query.Where(e => e.EntityName == entityName);

        if (!string.IsNullOrWhiteSpace(entityId))
            query = query.Where(e => e.EntityId == entityId);

        query = query.OrderByDescending(e => e.TimestampUtc);

        var total = await query.CountAsync(ct);
        var items = await query.Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }
}
