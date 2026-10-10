using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ai").WithTags("AI").RequireAuthorization();

        group.MapPost("/ask", async (AskAiRequest request, GeminiService service, CancellationToken ct) =>
            Results.Ok(await service.AskAsync(request, ct)));

        group.MapPost("/ian", async (IanAskRequest request, IanReportService service, CancellationToken ct) =>
            Results.Ok(await service.AskAsync(request, ct)));

        group.MapPost("/ian/analyze", async (IanAnalyzeRequest request, IanReportService service, CancellationToken ct) =>
            Results.Ok(await service.AnalyzeAsync(request, ct)));

        group.MapGet("/ian/report", async (
            string kind,
            string? competenceYm,
            string? fromYm,
            string? toYm,
            string? subject,
            string? matchKind,
            Guid? matchId,
            string? focus,
            string? categoryIds,
            string? chartKind,
            string? insight,
            IanReportService service,
            CancellationToken ct) =>
        {
            var query = new IanReportQuery(
                kind, competenceYm, fromYm, toYm, subject, matchKind, matchId, focus, categoryIds, chartKind, insight);
            return Results.Ok(await service.GetReportAsync(query, ct));
        });

        return app;
    }
}
