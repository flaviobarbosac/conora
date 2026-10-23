using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Conora.Domain.Ports;
using Conora.Infrastructure.Billing;
using Microsoft.Extensions.Options;

namespace Conora.Api.Endpoints;

/// <summary>
/// Google Play RTDN (Pub/Sub push) and App Store Server Notifications V2.
/// Apple is authenticated by the JWS chain to Apple's root; Google by <c>?k=</c> = StoreBilling:NotificationKey
/// and the state is re-read from the Play Developer API, so the body itself is never trusted.
/// </summary>
public static class StoreBillingWebhookEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IEndpointRouteBuilder MapStoreBillingWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/webhooks/store").WithTags("StoreBilling").AllowAnonymous();

        group.MapPost("/google-play", async (
            HttpRequest request,
            IStoreNotificationReader reader,
            IStoreSubscriptionBilling billing,
            IOptions<StoreBillingOptions> store,
            ILoggerFactory loggers,
            CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger("StoreBilling.google-play");
            if (string.IsNullOrEmpty(store.Value.NotificationKey)
                || !KeyMatches(request.Query["k"].FirstOrDefault(), store.Value.NotificationKey))
                return Results.Unauthorized();

            var body = await ReadBodyAsync<PubSubPush>(request, ct);
            return await ApplyAsync(() => reader.ReadGoogleAsync(body?.Message?.Data, ct), billing, logger, ct);
        });

        group.MapPost("/app-store", async (
            HttpRequest request,
            IStoreNotificationReader reader,
            IStoreSubscriptionBilling billing,
            ILoggerFactory loggers,
            CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger("StoreBilling.app-store");
            var body = await ReadBodyAsync<AppleNotificationBody>(request, ct);
            return await ApplyAsync(() => Task.FromResult(reader.ReadApple(body?.SignedPayload)), billing, logger, ct);
        });

        return app;
    }

    private static async Task<IResult> ApplyAsync(
        Func<Task<StoreNoticeCommand?>> read,
        IStoreSubscriptionBilling billing,
        ILogger logger,
        CancellationToken ct)
    {
        StoreNoticeCommand? notice;
        try
        {
            notice = await read();
        }
        catch (StoreNotificationRejectedException ex)
        {
            logger.LogWarning("Store notification rejected: {Reason}", ex.Message);
            return Results.BadRequest();
        }
        catch (InvalidOperationException ex)
        {
            // Store API unreachable or not configured: non-2xx makes the store retry later.
            logger.LogError(ex, "Store notification could not be read from the store API.");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (notice is null)
            return Results.Ok(new { status = "accepted", processed = false });

        await billing.ApplyNoticeAsync(notice, ct);
        return Results.Ok(new { status = "accepted", processed = true });
    }

    private static async Task<T?> ReadBodyAsync<T>(HttpRequest request, CancellationToken ct) where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(request.Body, JsonOptions, ct);
        }
        catch (JsonException)
        {
            return null;
        }
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

    private sealed record PubSubPush(PubSubMessage? Message);

    private sealed record PubSubMessage(string? Data);

    private sealed record AppleNotificationBody(string? SignedPayload);
}
