using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class LgpdEndpoints
{
    public static IEndpointRouteBuilder MapLgpdEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/me").WithTags("LGPD").RequireAuthorization();

        group.MapGet("/data", async (LgpdService service, CancellationToken ct) =>
            Results.Ok(await service.ExportAsync(ct)));

        group.MapPost("/delete", async (DeleteAccountRequest? request, LgpdService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(request, ct);
            return Results.NoContent();
        });

        return app;
    }
}
