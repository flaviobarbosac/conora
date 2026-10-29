using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class LgpdEndpoints
{
    public static IEndpointRouteBuilder MapLgpdEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/me").WithTags("LGPD").RequireAuthorization();

        // Queues the export; file is built overnight (~03:00) and stored on S3.
        group.MapPost("/data/request", async (LgpdService service, CancellationToken ct) =>
            Results.Ok(await service.RequestExportAsync(ct)));

        // Alias kept for older clients that still call GET /me/data.
        group.MapGet("/data", async (LgpdService service, CancellationToken ct) =>
            Results.Ok(await service.RequestExportAsync(ct)));

        group.MapGet("/data/status", async (LgpdService service, CancellationToken ct) =>
            Results.Ok(await service.GetStatusAsync(ct)));

        group.MapPost("/delete", async (DeleteAccountRequest? request, LgpdService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(request, ct);
            return Results.NoContent();
        });

        // One-time download: opaque token in query; no auth cookie/JWT required.
        app.MapGet("/lgpd/exports/download", async (string? t, LgpdService service, CancellationToken ct) =>
        {
            var file = await service.DownloadByTokenAsync(t ?? "", ct);
            if (file is null)
                return Results.NotFound(new { message = "Link inválido, expirado ou já utilizado." });

            return Results.File(
                file.Value.Content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                file.Value.FileName);
        }).AllowAnonymous().WithTags("LGPD");

        return app;
    }
}
