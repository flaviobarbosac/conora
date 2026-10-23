using Conora.Domain.Enums;
using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class ReceiptEndpoints
{
    public static IEndpointRouteBuilder MapReceiptEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/receipts").WithTags("Receipts").RequireAuthorization();

        group.MapPost("/", async (HttpRequest request, ReceiptIntakeService service, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Envie multipart/form-data com o campo file." });

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "Arquivo de imagem ou PDF obrigatório." });

            var channel = Enum.TryParse<ReceiptChannel>(form["channel"], ignoreCase: true, out var parsed)
                ? parsed
                : ReceiptChannel.App;

            var purpose = Enum.TryParse<ReceiptPurpose>(form["purpose"], ignoreCase: true, out var purposeParsed)
                ? purposeParsed
                : ReceiptPurpose.Expense;

            await using var stream = file.OpenReadStream();
            var created = await service.CreateAsync(
                stream,
                file.ContentType ?? "image/jpeg",
                channel,
                ct,
                purpose: purpose);
            return Results.Created($"/receipts/{created.Id}", created);
        });

        group.MapGet("/{id:guid}", async (Guid id, ReceiptIntakeService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(id, ct)));

        group.MapGet("/categories", async (ReceiptIntakeService service, CancellationToken ct) =>
            Results.Ok(await service.ListClassifiableCategoriesAsync(ct)));

        group.MapPost("/{id:guid}/classify", async (
            Guid id,
            ClassifyReceiptRequest request,
            ReceiptIntakeService service,
            CancellationToken ct) =>
            Results.Ok(await service.ClassifyAsync(id, request, ct)));

        group.MapPost("/{id:guid}/link-supplier", async (
            Guid id,
            LinkSupplierRequest request,
            ReceiptIntakeService service,
            CancellationToken ct) =>
            Results.Ok(await service.LinkSupplierAsync(id, request, ct)));

        group.MapPost("/{id:guid}/confirm", async (
            Guid id,
            ConfirmReceiptRequest request,
            ReceiptIntakeService service,
            CancellationToken ct) =>
            Results.Ok(await service.ConfirmAsync(id, request, ct)));

        group.MapPost("/{id:guid}/confirm-budget", async (
            Guid id,
            ConfirmReceiptRequest request,
            ReceiptIntakeService service,
            CancellationToken ct) =>
            Results.Ok(await service.ConfirmBudgetAsync(id, request, ct)));

        return app;
    }
}
