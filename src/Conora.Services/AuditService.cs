using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class AuditService
{
    private readonly IAuditEventRepository _audits;

    public AuditService(IAuditEventRepository audits)
    {
        _audits = audits;
    }

    public async Task<PagedResponse<AuditEventResponse>> QueryAsync(AuditEventFilter filter, CancellationToken ct)
    {
        var skip = Math.Max(filter.Skip, 0);
        var take = Math.Clamp(filter.Take, 1, 500);

        var (items, totalCount) = await _audits.QueryAsync(filter.EntityName, filter.EntityId, skip, take, ct);

        var responses = items.Select(e => new AuditEventResponse(
            e.Id, e.EntityName, e.EntityId, e.Action, e.Actor, e.TimestampUtc, e.CorrelationId, e.DetailsJson)).ToList();

        return new PagedResponse<AuditEventResponse>(responses, skip, take, totalCount);
    }
}
