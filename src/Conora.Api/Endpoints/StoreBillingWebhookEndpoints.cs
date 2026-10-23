using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Conora.Domain.Ports;
using Conora.Infrastructure.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conora.Api.Endpoints;

/// <summary>
/// Google Play RTDN and App Store Server Notifications — Clampfy-shaped store billing layer.
/// Accepts productId (+ purchaseToken / transactionId) and maps via StoreBillingOptions;
/// production must still verify Pub/Sub JWT / Apple JWS before trusting the body.
/// </summary>
public static class StoreBillingWebhookEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IEndpointRouteBuilder MapStoreBillingWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/webhooks/store").WithTags("StoreBilling").AllowAnonymous();

        group.MapPost("/google-play", (
            HttpRequest request,
            IStoreSubscriptionBilling billing,
            IOptions<StoreBillingOptions> store,
            ILoggerFactory loggers,
            CancellationToken ct) =>
            HandleAsync(request, billing, store.Value, loggers, StoreBillingChannel.Google, ct));

        group.MapPost("/app-store", (
            HttpRequest request,
            IStoreSubscriptionBilling billing,
            IOptions<StoreBillingOptions> store,
            ILoggerFactory loggers,
            CancellationToken ct) =>
            HandleAsync(request, billing, store.Value, loggers, StoreBillingChannel.Apple, ct));

        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        IStoreSubscriptionBilling billing,
        StoreBillingOptions store,
        ILoggerFactory loggers,
        StoreBillingChannel channel,
        CancellationToken ct)
    {
        var storeLabel = channel == StoreBillingChannel.Google ? "google-play" : "app-store";
        var logger = loggers.CreateLogger($"StoreBilling.{storeLabel}");

        if (channel == StoreBillingChannel.Google
            && !string.IsNullOrEmpty(store.NotificationKey)
            && !KeyMatches(request.Query["k"].FirstOrDefault(), store.NotificationKey))
        {
            return Results.Unauthorized();
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(ct);
        logger.LogInformation("{Store} notification received {Length} bytes.", storeLabel, body.Length);

        if (string.IsNullOrWhiteSpace(body))
            return Results.Ok(new { status = "accepted", processed = false });

        try
        {
            var payload = JsonSerializer.Deserialize<StoreWebhookPayload>(body, JsonOptions);
            if (payload is null)
                return Results.Ok(new { status = "accepted", processed = false });

            var productId = payload.ProductId?.Trim() ?? "";
            var purchaseToken = payload.PurchaseToken?.Trim()
                ?? payload.SubscriptionId?.Trim()
                ?? payload.TransactionId?.Trim()
                ?? "";
            var transactionId = payload.TransactionId?.Trim() ?? purchaseToken;
            var evento = string.IsNullOrWhiteSpace(payload.Evento) ? "pago" : payload.Evento.Trim();

            if (payload.UsuarioId is Guid userId
                && !string.IsNullOrWhiteSpace(productId)
                && !string.IsNullOrWhiteSpace(purchaseToken)
                && billing.TryMapProduct(productId, out _))
            {
                await billing.ApplyNoticeAsync(
                    new StoreNoticeCommand(
                        channel,
                        evento,
                        productId,
                        transactionId,
                        payload.SubscriptionId,
                        userId,
                        payload.EmTrial),
                    ct);
                return Results.Ok(new { status = "accepted", processed = true });
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Store} payload could not activate a plan; acknowledged anyway.", storeLabel);
        }

        return Results.Ok(new { status = "accepted", processed = false });
    }

    private static bool KeyMatches(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(provided))
            return false;
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length
            && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private sealed record StoreWebhookPayload(
        Guid? UsuarioId,
        string? ProductId,
        string? PurchaseToken,
        string? TransactionId,
        string? SubscriptionId,
        string? Evento,
        bool EmTrial = false);
}
