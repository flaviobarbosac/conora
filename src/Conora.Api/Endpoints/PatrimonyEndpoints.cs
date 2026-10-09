using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class PatrimonyEndpoints
{
    public static IEndpointRouteBuilder MapPatrimonyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/patrimony").WithTags("Patrimony").RequireAuthorization();

        group.MapGet("/", async (PatrimonyService service, CancellationToken ct) =>
            Results.Ok(await service.GetSummaryAsync(ct)));

        group.MapGet("/reserve", async (PatrimonyService service, CancellationToken ct, string? competenceYm = null) =>
            Results.Ok(await service.GetReserveAsync(competenceYm, ct)));

        group.MapPost("/items", async (PatrimonyItemRequest request, PatrimonyService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(request, ct);
            return Results.Created($"/patrimony/items/{created.Id}", created);
        });

        group.MapPut("/items/{id:guid}", async (Guid id, UpdatePatrimonyItemRequest request, PatrimonyService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, ct)));

        group.MapDelete("/items/{id:guid}", async (Guid id, PatrimonyService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        return app;
    }
}
