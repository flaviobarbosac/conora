using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/imports").WithTags("Imports").RequireAuthorization();

        group.MapPost("/preview", async (ImportPreviewRequest request, ImportService service, CancellationToken ct) =>
        {
            var preview = await service.PreviewAsync(request, ct);
            return Results.Created($"/imports/{preview.BatchId}", preview);
        });

        group.MapGet("/{id:guid}", async (Guid id, ImportService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(id, ct)));

        group.MapPut("/{id:guid}/rows/{rowId:guid}", async (Guid id, Guid rowId, SetImportRowRequest request, ImportService service, CancellationToken ct) =>
            Results.Ok(await service.SetRowAsync(id, rowId, request, ct)));

        group.MapPost("/{id:guid}/commit", async (Guid id, CommitImportRequest request, ImportService service, CancellationToken ct) =>
            Results.Ok(await service.CommitAsync(id, request, ct)));

        return app;
    }
}
