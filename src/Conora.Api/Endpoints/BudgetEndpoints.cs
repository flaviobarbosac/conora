using Conora.Domain.Enums;
using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class BudgetEndpoints
{
    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/budgets").WithTags("Budgets").RequireAuthorization();

        group.MapGet("/year/{year:int}", async (
            int year,
            BudgetService service,
            CancellationToken ct,
            SharingScope? scope = null) =>
            Results.Ok(await service.GetYearAsync(year, ct, scope)));

        group.MapGet("/{competenceYm}/breakdown/{categoryId:guid}", async (
            string competenceYm,
            Guid categoryId,
            BudgetService service,
            CancellationToken ct,
            SharingScope? scope = null) =>
            Results.Ok(await service.GetCategoryBreakdownAsync(competenceYm, categoryId, ct, scope)));

        group.MapGet("/{competenceYm}", async (
            string competenceYm,
            BudgetService service,
            CancellationToken ct,
            SharingScope? scope = null) =>
            Results.Ok(await service.GetAsync(competenceYm, ct, scope)));

        group.MapPut("/{competenceYm}", async (
            string competenceYm,
            UpsertBudgetRequest request,
            BudgetService service,
            CancellationToken ct,
            SharingScope? scope = null) =>
            Results.Ok(await service.UpsertAsync(competenceYm, request, ct, scope)));

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

        group.MapDelete("/{competenceYm}/lines/{CategoryId:guid}", async (string competenceYm, Guid CategoryId, BudgetService service, CancellationToken ct) =>
            Results.Ok(await service.DeleteLineAsync(competenceYm, CategoryId, ct)));

        return app;
    }
}
