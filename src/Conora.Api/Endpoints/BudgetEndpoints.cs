using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class BudgetEndpoints
{
    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/budgets").WithTags("Budgets").RequireAuthorization();

        group.MapGet("/year/{year:int}", async (int year, BudgetService service, CancellationToken ct) =>
            Results.Ok(await service.GetYearAsync(year, ct)));

        group.MapGet("/{competenceYm}", async (string competenceYm, BudgetService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(competenceYm, ct)));

        group.MapPut("/{competenceYm}", async (string competenceYm, UpsertBudgetRequest request, BudgetService service, CancellationToken ct) =>
            Results.Ok(await service.UpsertAsync(competenceYm, request, ct)));

        group.MapPost("/{competenceYm}/copy-previous", async (string competenceYm, BudgetService service, CancellationToken ct) =>
            Results.Ok(await service.CopyFromPreviousAsync(competenceYm, ct)));

        group.MapDelete("/{competenceYm}/lines/{categoryId:guid}", async (string competenceYm, Guid categoryId, BudgetService service, CancellationToken ct) =>
            Results.Ok(await service.DeleteLineAsync(competenceYm, categoryId, ct)));

        return app;
    }
}
