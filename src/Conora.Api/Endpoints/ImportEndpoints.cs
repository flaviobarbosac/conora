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

        group.MapPost("/upload", async (HttpRequest request, ImportService service, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Envie multipart/form-data com o campo file." });

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "Arquivo do extrato obrigatório." });

            var competenceYm = form["competenceYm"].ToString();
            if (string.IsNullOrWhiteSpace(competenceYm))
                return Results.BadRequest(new { error = "competenceYm é obrigatório." });

            await using var stream = file.OpenReadStream();
            var preview = await service.UploadAsync(
                file.FileName,
                file.ContentType ?? "application/octet-stream",
                stream,
                competenceYm,
                ct);
            return Results.Created($"/imports/{preview.BatchId}", preview);
        });

        group.MapGet("/open", async (string competenceYm, ImportService service, CancellationToken ct) =>
        {
            var open = await service.GetOpenAsync(competenceYm, ct);
            return open is null ? Results.NoContent() : Results.Ok(open);
        });

        group.MapGet("/{id:guid}", async (Guid id, ImportService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(id, ct)));

        group.MapGet("/{id:guid}/current", async (Guid id, ImportService service, CancellationToken ct) =>
        {
            var current = await service.GetCurrentAsync(id, ct);
            return current is null ? Results.NoContent() : Results.Ok(current);
        });

        group.MapGet("/{id:guid}/report", async (Guid id, ImportService service, CancellationToken ct) =>
            Results.Ok(await service.GetReportAsync(id, ct)));

        group.MapPost("/{id:guid}/account", async (Guid id, BindImportAccountRequest request, ImportService service, CancellationToken ct) =>
            Results.Ok(await service.BindAccountAsync(id, request, ct)));

        group.MapPost("/{id:guid}/discard", async (Guid id, ImportService service, CancellationToken ct) =>
        {
            await service.DiscardAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPut("/{id:guid}/rows/{rowId:guid}", async (Guid id, Guid rowId, SetImportRowRequest request, ImportService service, CancellationToken ct) =>
            Results.Ok(await service.SetRowAsync(id, rowId, request, ct)));

        group.MapPost("/{id:guid}/rows/{rowId:guid}/commit", async (
            Guid id,
            Guid rowId,
            ClassifyImportRowRequest request,
            ImportService service,
            CancellationToken ct) =>
        {
            var next = await service.CommitRowAsync(id, rowId, request, ct);
            return next is null ? Results.NoContent() : Results.Ok(next);
        });

        group.MapPost("/{id:guid}/rows/{rowId:guid}/defer", async (Guid id, Guid rowId, ImportService service, CancellationToken ct) =>
        {
            var next = await service.DeferRowAsync(id, rowId, ct);
            return next is null ? Results.NoContent() : Results.Ok(next);
        });

        group.MapPost("/{id:guid}/rows/{rowId:guid}/ignore", async (Guid id, Guid rowId, ImportService service, CancellationToken ct) =>
        {
            var next = await service.IgnoreRowAsync(id, rowId, ct);
            return next is null ? Results.NoContent() : Results.Ok(next);
        });

        group.MapPost("/{id:guid}/rows/{rowId:guid}/skip-duplicate", async (Guid id, Guid rowId, ImportService service, CancellationToken ct) =>
        {
            await service.SkipDuplicateAsync(id, rowId, ct);
            var next = await service.GetCurrentAsync(id, ct);
            return next is null ? Results.NoContent() : Results.Ok(next);
        });

        group.MapPost("/{id:guid}/commit", async (Guid id, CommitImportRequest request, ImportService service, CancellationToken ct) =>
            Results.Ok(await service.CommitAsync(id, request, ct)));

        return app;
    }
}
