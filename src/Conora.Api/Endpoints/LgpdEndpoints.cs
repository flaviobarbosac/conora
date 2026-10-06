using Conora.Services;

namespace Conora.Api.Endpoints;

public static class LgpdEndpoints
{
    public static IEndpointRouteBuilder MapLgpdEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/me").WithTags("LGPD").RequireAuthorization();

        group.MapGet("/data", async (LgpdService service, CancellationToken ct) =>
            Results.Ok(await service.ExportAsync(ct)));

        group.MapPost("/delete", async (LgpdService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(ct);
            return Results.NoContent();
        });

        return app;
    }
}
