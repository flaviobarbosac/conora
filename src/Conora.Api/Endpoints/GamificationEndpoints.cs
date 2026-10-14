using Conora.Services;

namespace Conora.Api.Endpoints;

public static class GamificationEndpoints
{
    public static IEndpointRouteBuilder MapGamificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/gamification").WithTags("Gamification").RequireAuthorization();

        group.MapGet("/journey", async (GamificationService service, CancellationToken ct) =>
            Results.Ok(await service.GetJourneyAsync(ct)));

        group.MapGet("/achievements", async (GamificationService service, CancellationToken ct) =>
            Results.Ok(await service.GetAchievementsAsync(ct)));

        group.MapGet("/home", async (GamificationService service, CancellationToken ct, string? competenceYm = null) =>
            Results.Ok(await service.GetHomeProgressAsync(competenceYm, ct)));

        group.MapGet("/reserve", async (GamificationService service, CancellationToken ct, string? competenceYm = null) =>
            Results.Ok(await service.GetReserveThermometerAsync(competenceYm, ct)));

        group.MapGet("/months/{competenceYm}/summary", async (string competenceYm, GamificationService service, CancellationToken ct) =>
            Results.Ok(await service.GetMonthCloseSummaryAsync(competenceYm, ct)));

        group.MapGet("/retrospective/{year:int}", async (int year, GamificationService service, CancellationToken ct) =>
            Results.Ok(await service.GetYearRetrospectiveAsync(year, ct)));

        return app;
    }
}
