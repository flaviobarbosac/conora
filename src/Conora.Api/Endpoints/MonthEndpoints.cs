using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class MonthEndpoints
{
    public static IEndpointRouteBuilder MapMonthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/months").WithTags("Months").RequireAuthorization();

        group.MapGet("/", async (MonthService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ct)));

        group.MapGet("/{competenceYm}", async (string competenceYm, MonthService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(competenceYm, ct)));

        group.MapPost("/{competenceYm}/close", async (string competenceYm, MonthService service, CancellationToken ct) =>
            Results.Ok(await service.CloseAsync(competenceYm, ct)));

        group.MapPost("/{competenceYm}/reopen", async (string competenceYm, ReopenMonthRequest request, MonthService service, CancellationToken ct) =>
            Results.Ok(await service.ReopenAsync(competenceYm, request, ct)));

        return app;
    }
}
