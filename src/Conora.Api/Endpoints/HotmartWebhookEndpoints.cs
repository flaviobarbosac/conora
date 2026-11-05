using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Conora.Infrastructure.Billing;
using Conora.Services;
using Microsoft.Extensions.Options;

namespace Conora.Api.Endpoints;

/// <summary>
/// Hotmart webhook 2.0. Authenticated by the X-HOTMART-HOTTOK header (or body hottok).
/// PURCHASE_COMPLETE schedules the 90-day release for the 8th day.
/// PURCHASE_CANCELED and PURCHASE_PROTEST remove that access.
/// </summary>
public static class HotmartWebhookEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IEndpointRouteBuilder MapHotmartWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/hotmart", async (
            HttpRequest request,
            HotmartCourseService courses,
            IOptions<HotmartOptions> options,
            CancellationToken ct) =>
        {
            var configured = options.Value.Hottok;
            if (string.IsNullOrEmpty(configured))
                return Results.Unauthorized();

            HotmartWebhookBody? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync<HotmartWebhookBody>(request.Body, JsonOptions, ct);
            }
            catch (JsonException)
            {
                return Results.BadRequest();
            }

            var provided = request.Headers["X-HOTMART-HOTTOK"].FirstOrDefault() ?? body?.Hottok;
            if (!KeyMatches(provided, configured))
                return Results.Unauthorized();

            var processed = await courses.ReceiveAsync(
                body?.Event,
                body?.Data?.Buyer?.Email,
                body?.Data?.Purchase?.Transaction,
                body?.Data?.Product?.Id,
                body?.Data?.Purchase?.AcquiredAtUtc(),
                ct);
            return Results.Ok(new { status = "accepted", processed });
        }).AllowAnonymous().WithTags("Hotmart");

        return app;
    }

    private static bool KeyMatches(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(provided))
            return false;
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private sealed class HotmartWebhookBody
    {
        public string? Event { get; set; }
        public string? Hottok { get; set; }
        public HotmartData? Data { get; set; }
    }

    private sealed class HotmartData
    {
        public HotmartProduct? Product { get; set; }
        public HotmartBuyer? Buyer { get; set; }
        public HotmartPurchase? Purchase { get; set; }
    }

    private sealed class HotmartProduct
    {
        public long? Id { get; set; }
    }

    private sealed class HotmartBuyer
    {
        public string? Email { get; set; }
    }

    private sealed class HotmartPurchase
    {
        public string? Transaction { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("approved_date")]
        public long? ApprovedDate { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("order_date")]
        public string? OrderDate { get; set; }

        public DateTime? AcquiredAtUtc()
        {
            if (ApprovedDate is > 0)
                return DateTimeOffset.FromUnixTimeMilliseconds(ApprovedDate.Value).UtcDateTime;
            if (long.TryParse(OrderDate, out var orderMs) && orderMs > 1_000_000_000_000)
                return DateTimeOffset.FromUnixTimeMilliseconds(orderMs).UtcDateTime;
            return null;
        }
    }
}
