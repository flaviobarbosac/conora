using Conora.Services;
using Conora.Services.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Conora.Api.Endpoints;

public static class EntryEndpoints
{
    public static IEndpointRouteBuilder MapEntryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/entries").WithTags("Entries").RequireAuthorization();

        group.MapGet("/", async ([AsParameters] EntryFilter filter, EntryService service, CancellationToken ct) =>
            Results.Ok(await service.SearchAsync(filter, ct)));

        group.MapGet("/suggest-category", async (string description, EntryService service, CancellationToken ct) =>
            Results.Ok(await service.SuggestCategoryAsync(description, ct)));

        group.MapGet("/{id:guid}", async (Guid id, EntryService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(id, ct)));

        group.MapPost("/", async (CreateEntryRequest request, EntryService service, CancellationToken ct) =>
            Results.Created("/entries", await service.CreateAsync(request, ct)));

        group.MapPut("/{id:guid}", async (Guid id, UpdateEntryRequest request, EntryService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, ct)));

        group.MapDelete("/{id:guid}", async (Guid id, EntryService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/duplicate", async (Guid id, EntryService service, CancellationToken ct, [FromQuery] DateTime? occurredAt = null) =>
            Results.Created("/entries", await service.DuplicateAsync(id, occurredAt, ct)));

        return app;
    }
}
