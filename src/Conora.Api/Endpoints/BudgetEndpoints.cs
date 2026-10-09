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

        group.MapPost("/{competenceYm}/copy-previous", async (
            string competenceYm,
            CopyPreviousBudgetRequest? request,
            BudgetService service,
            CancellationToken ct) =>
            Results.Ok(await service.CopyFromPreviousAsync(competenceYm, request, ct)));

        group.MapPost("/{competenceYm}/repeat", async (
            string competenceYm,
            RepeatBudgetRequest request,
            BudgetService service,
            CancellationToken ct) =>
            Results.Ok(await service.RepeatAsync(competenceYm, request, ct)));

        group.MapPost("/{competenceYm}/installments", async (
            string competenceYm,
            InstallmentBudgetRequest request,
            BudgetService service,
            CancellationToken ct) =>
            Results.Ok(await service.InstallmentAsync(competenceYm, request, ct)));

        group.MapDelete("/{competenceYm}/lines/{ChartAccountId:guid}", async (string competenceYm, Guid ChartAccountId, BudgetService service, CancellationToken ct) =>
            Results.Ok(await service.DeleteLineAsync(competenceYm, ChartAccountId, ct)));

        return app;
    }
}
