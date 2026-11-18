using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class MonthEndpoints
{
    public static IEndpointRouteBuilder MapMonthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/months").WithTags("Months").RequireAuthorization();

        group.MapGet("/shared-with-me", async (MonthShareService service, CancellationToken ct) =>
            Results.Ok(await service.ListSharedWithMeAsync(ct)));

        group.MapGet("/shared", async (MonthShareService service, CancellationToken ct) =>
            Results.Ok(await service.ListMineAsync(ct)));

        group.MapGet("/", async (MonthService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ct)));

        group.MapGet("/{competenceYm}", async (string competenceYm, MonthService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(competenceYm, ct)));

        group.MapPost("/{competenceYm}/share", async (
            string competenceYm,
            MonthShareService service,
            CancellationToken ct) =>
        {
            await service.ShareWithOperatorsAsync(competenceYm, ct);
            return Results.NoContent();
        });

        group.MapDelete("/{competenceYm}/share", async (
            string competenceYm,
            MonthShareService service,
            CancellationToken ct) =>
        {
            await service.UnshareAsync(competenceYm, ct);
            return Results.NoContent();
        });

        group.MapPost("/{competenceYm}/close", async (string competenceYm, MonthService service, CancellationToken ct) =>
            Results.Ok(await service.CloseAsync(competenceYm, ct)));

        group.MapPost("/{competenceYm}/reopen", async (string competenceYm, ReopenMonthRequest request, MonthService service, CancellationToken ct) =>
            Results.Ok(await service.ReopenAsync(competenceYm, request, ct)));

        return app;
    }
}
