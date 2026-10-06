using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class PlanEndpoints
{
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/plan").WithTags("Plan").RequireAuthorization();

        group.MapGet("/", async (PlanService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(ct)));

        group.MapPost("/subscribe", async (SubscribeRequest request, PlanService service, CancellationToken ct) =>
            Results.Ok(await service.SubscribeAsync(request, ct)));

        return app;
    }
}
