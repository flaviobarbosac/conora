using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/audit-events", async (
            AuditService service, CancellationToken ct,
            string? entityName = null, string? entityId = null, int skip = 0, int take = 50) =>
        {
            var filter = new AuditEventFilter(entityName, entityId, skip, take);
            var result = await service.QueryAsync(filter, ct);
            return Results.Ok(result);
        }).WithTags("Audit").RequireAuthorization();

        return app;
    }
}
