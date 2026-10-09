using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class CreditCardEndpoints
{
    public static IEndpointRouteBuilder MapCreditCardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/credit-cards").WithTags("CreditCards").RequireAuthorization();

        group.MapGet("/", async (CreditCardService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ct)));

        group.MapGet("/{id:guid}", async (Guid id, CreditCardService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(id, ct)));

        group.MapPost("/", async (CreateCardRequest request, CreditCardService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(request, ct);
            return Results.Created($"/credit-cards/{created.Id}", created);
        });

        group.MapPut("/{id:guid}", async (Guid id, CreateCardRequest request, CreditCardService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, ct)));

        group.MapDelete("/{id:guid}", async (Guid id, CreditCardService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/purchases", async (Guid id, CreditCardService service, CancellationToken ct, string? competenceYm = null) =>
            Results.Ok(await service.ListPurchasesAsync(id, competenceYm, ct)));

        group.MapPost("/{id:guid}/purchases", async (Guid id, CardPurchaseRequest request, CreditCardService service, CancellationToken ct) =>
        {
            var created = await service.AddPurchaseAsync(id, request, ct);
            return Results.Created($"/credit-cards/{id}/purchases/{created.Id}", created);
        });

        // Refund (estorno) of a purchase.
        group.MapDelete("/{id:guid}/purchases/{purchaseId:guid}", async (Guid id, Guid purchaseId, CreditCardService service, CancellationToken ct) =>
        {
            await service.RefundPurchaseAsync(id, purchaseId, ct);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/invoices", async (Guid id, CreditCardService service, CancellationToken ct) =>
            Results.Ok(await service.ListInvoicesAsync(id, ct)));

        group.MapPost("/{id:guid}/invoices/{competenceYm}/pay", async (
            Guid id, string competenceYm, PayInvoiceRequest request, CreditCardService service, CancellationToken ct) =>
            Results.Ok(await service.PayInvoiceAsync(id, competenceYm, request, ct)));

        return app;
    }
}
