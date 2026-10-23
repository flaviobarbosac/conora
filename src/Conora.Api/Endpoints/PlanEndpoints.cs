using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class PlanEndpoints
{
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/plan").WithTags("Plan").RequireAuthorization();

        group.MapGet("/", async (PlanService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(ct)));

        // Local stub purchase: never reachable in Production, where only the stores activate plans.
        group.MapPost("/subscribe", async (SubscribeRequest request, PlanService service, IHostEnvironment env, CancellationToken ct) =>
            env.IsDevelopment() || env.IsEnvironment("Testing")
                ? Results.Ok(await service.SubscribeAsync(request, ct))
                : Results.NotFound());

        // Called by the app right after the App Store / Google Play purchase sheet (and on "restore").
        group.MapPost("/store/confirm", async (
            StoreConfirmRequest request,
            IStoreSubscriptionBilling billing,
            ITenantContext tenant,
            PlanService service,
            CancellationToken ct) =>
        {
            if (!Enum.IsDefined(typeof(StoreBillingChannel), request.Channel))
                throw new ValidationException("channel", "Canal de loja inválido.");
            var usuarioId = tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");
            await billing.ConfirmAsync(
                new StorePurchaseCommand(
                    (StoreBillingChannel)request.Channel,
                    request.ProductId ?? "",
                    request.TransactionId ?? "",
                    request.PurchaseToken ?? "",
                    usuarioId),
                ct);
            return Results.Ok(await service.GetAsync(ct));
        });

        return app;
    }
}
