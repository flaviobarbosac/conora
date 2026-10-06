using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class LifeProjectEndpoints
{
    public static IEndpointRouteBuilder MapLifeProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/life-projects").WithTags("LifeProjects").RequireAuthorization();

        group.MapGet("/", async (LifeProjectService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ct)));

        group.MapGet("/{id:guid}", async (Guid id, LifeProjectService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(id, ct)));

        group.MapPost("/", async (LifeProjectRequest request, LifeProjectService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(request, ct);
            return Results.Created($"/life-projects/{created.Id}", created);
        });

        group.MapPut("/{id:guid}", async (Guid id, LifeProjectRequest request, LifeProjectService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, ct)));

        group.MapDelete("/{id:guid}", async (Guid id, LifeProjectService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/contributions", async (Guid id, ProjectContributionRequest request, LifeProjectService service, CancellationToken ct) =>
            Results.Ok(await service.ContributeAsync(id, request, ct)));

        return app;
    }
}
