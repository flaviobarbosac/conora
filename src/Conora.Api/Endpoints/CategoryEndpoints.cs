using Conora.Domain.Enums;
using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/categories").WithTags("Categories").RequireAuthorization();

        group.MapGet("/", async (
            CategoryService service,
            CancellationToken ct,
            CategorySection? section = null,
            bool includeInactive = false,
            bool analyticalOnly = false,
            bool includePending = false) =>
            Results.Ok(await service.ListAsync(section, includeInactive, analyticalOnly, ct, includePending)));

        group.MapPost("/", async (CreateCategoryRequest request, CategoryService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(request, ct);
            return Results.Created($"/categories/{created.Id}", created);
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateCategoryRequest request, CategoryService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, ct)));

        group.MapPut("/{id:guid}/active", async (Guid id, SetCategoryActiveRequest request, CategoryService service, CancellationToken ct) =>
            Results.Ok(await service.SetActiveAsync(id, request, ct)));

        group.MapPut("/{id:guid}/sharing", async (Guid id, UpdateCategorySharingRequest request, CategoryService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateSharingAsync(id, request, ct)));

        group.MapPost("/{id:guid}/approve", async (Guid id, CategoryService service, CancellationToken ct) =>
            Results.Ok(await service.ApproveAsync(id, ct)));

        group.MapPost("/{id:guid}/reject", async (Guid id, CategoryService service, CancellationToken ct) =>
        {
            await service.RejectAsync(id, ct);
            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}", async (Guid id, CategoryService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        return app;
    }
}
