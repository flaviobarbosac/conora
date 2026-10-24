using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Conora.Infrastructure.Billing;
using Conora.Services;
using Microsoft.Extensions.Options;

namespace Conora.Api.Endpoints;

/// <summary>
/// Hotmart webhook 2.0. Authenticated by the X-HOTMART-HOTTOK header (or body hottok).
/// PURCHASE_APPROVED and PURCHASE_COMPLETE mark the buyer as a training student.
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
    }
}
