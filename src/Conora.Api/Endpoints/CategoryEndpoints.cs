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
            bool analyticalOnly = false) =>
            Results.Ok(await service.ListAsync(section, includeInactive, analyticalOnly, ct)));

        group.MapPost("/", async (CreateCategoryRequest request, CategoryService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(request, ct);
            return Results.Created($"/categories/{created.Id}", created);
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateCategoryRequest request, CategoryService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, ct)));

        group.MapDelete("/{id:guid}", async (Guid id, CategoryService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        return app;
    }
}
