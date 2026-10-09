using Conora.Services;

namespace Conora.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var dashboard = app.MapGroup("/dashboard").WithTags("Dashboard").RequireAuthorization();

        dashboard.MapGet("/{competenceYm}", async (string competenceYm, DashboardService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(competenceYm, ct)));

        dashboard.MapGet("/{competenceYm}/alerts", async (string competenceYm, DashboardService service, CancellationToken ct) =>
            Results.Ok(await service.GetAlertsAsync(competenceYm, ct)));

        app.MapGet("/reports/monthly/{competenceYm}", async (string competenceYm, DashboardService service, CancellationToken ct) =>
            Results.Ok(await service.GetReportAsync(competenceYm, ct)))
            .WithTags("Reports").RequireAuthorization();

        // Exports stay available in read-only mode (spec v1.1 §4).
        var export = app.MapGroup("/export").WithTags("Export").RequireAuthorization();

        export.MapGet("/entries", async (DashboardService service, CancellationToken ct, string? competenceYm = null) =>
        {
            var file = await service.ExportEntriesAsync(competenceYm, ct);
            return Results.File(file.Content, file.ContentType, file.FileName);
        });

        export.MapGet("/summary/{competenceYm}", async (string competenceYm, DashboardService service, CancellationToken ct) =>
        {
            var file = await service.ExportSummaryAsync(competenceYm, ct);
            return Results.File(file.Content, file.ContentType, file.FileName);
        });

        return app;
    }
}
