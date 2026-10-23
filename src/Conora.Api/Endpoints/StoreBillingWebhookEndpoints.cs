using System.Text;
using System.Text.Json;
using Conora.Domain.Enums;
using Conora.Services;
using Conora.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace Conora.Api.Endpoints;

/// <summary>
/// Google Play RTDN and App Store Server Notifications.
/// Stub accepts a JSON body with usuarioId, plan, purchaseToken to activate the plan;
/// production must verify JWT / Pub/Sub and map the store ids before calling PlanService.
/// </summary>
public static class StoreBillingWebhookEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IEndpointRouteBuilder MapStoreBillingWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/webhooks/store").WithTags("StoreBilling").AllowAnonymous();

        group.MapPost("/google-play", (HttpRequest request, PlanService plans, ILoggerFactory loggers, CancellationToken ct) =>
            HandleAsync(request, plans, loggers, "google-play", ct));

        group.MapPost("/app-store", (HttpRequest request, PlanService plans, ILoggerFactory loggers, CancellationToken ct) =>
            HandleAsync(request, plans, loggers, "app-store", ct));

        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        PlanService plans,
        ILoggerFactory loggers,
        string store,
        CancellationToken ct)
    {
        var logger = loggers.CreateLogger($"StoreBilling.{store}");
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(ct);
        logger.LogInformation("{Store} notification received {Length} bytes.", store, body.Length);

        if (string.IsNullOrWhiteSpace(body))
            return Results.Ok(new { status = "accepted", processed = false });

        try
        {
            var payload = JsonSerializer.Deserialize<StoreWebhookPayload>(body, JsonOptions);
            if (payload?.UsuarioId is Guid userId
                && !string.IsNullOrWhiteSpace(payload.PurchaseToken)
                && Enum.TryParse<PlanKind>(payload.Plan, ignoreCase: true, out var plan))
            {
                await plans.ApplyStoreConfirmationAsync(
                    new StorePurchaseConfirmation(userId, plan, payload.PurchaseToken, store),
                    ct);
                return Results.Ok(new { status = "accepted", processed = true });
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Store} payload could not activate a plan; acknowledged anyway.", store);
        }

        return Results.Ok(new { status = "accepted", processed = false });
    }

    private sealed record StoreWebhookPayload(Guid? UsuarioId, string? Plan, string? PurchaseToken);
}
