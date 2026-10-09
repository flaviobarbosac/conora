using Conora.Domain.Enums;
using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class ChartAccountEndpoints
{
    public static IEndpointRouteBuilder MapChartAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/chart-accounts").WithTags("ChartAccounts").RequireAuthorization();

        group.MapGet("/", async (
            ChartAccountService service,
            CancellationToken ct,
            ChartSection? section = null,
            bool includeInactive = false,
            bool analyticalOnly = false) =>
            Results.Ok(await service.ListAsync(section, includeInactive, analyticalOnly, ct)));

        group.MapPost("/", async (CreateChartAccountRequest request, ChartAccountService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(request, ct);
            return Results.Created($"/chart-accounts/{created.Id}", created);
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateChartAccountRequest request, ChartAccountService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, ct)));

        group.MapDelete("/{id:guid}", async (Guid id, ChartAccountService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        return app;
    }
}
