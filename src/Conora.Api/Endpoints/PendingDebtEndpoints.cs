using Conora.Services;

namespace Conora.Api.Endpoints;

public static class PendingDebtEndpoints
{
    public static IEndpointRouteBuilder MapPendingDebtEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/pending-debts").WithTags("PendingDebts").RequireAuthorization();

        group.MapGet("/", async (PendingDebtService service, CancellationToken ct) =>
            Results.Ok(await service.ListOpenAsync(ct)));

        return app;
    }
}
