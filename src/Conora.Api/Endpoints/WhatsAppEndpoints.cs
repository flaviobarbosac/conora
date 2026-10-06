using System.Security.Cryptography;
using System.Text;
using Conora.Domain.Enums;
using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class WhatsAppEndpoints
{
    public static IEndpointRouteBuilder MapWhatsAppEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/whatsapp").WithTags("WhatsApp").RequireAuthorization();

        group.MapGet("/link", async (WhatsAppService service, CancellationToken ct) =>
            await service.GetLinkAsync(ct) is { } link ? Results.Ok(link) : Results.NoContent());

        group.MapPost("/link", async (LinkWhatsAppRequest request, WhatsAppService service, CancellationToken ct) =>
            Results.Ok(await service.LinkAsync(request, ct)));

        group.MapDelete("/link", async (WhatsAppService service, CancellationToken ct) =>
        {
            await service.UnlinkAsync(ct);
            return Results.NoContent();
        });

        group.MapGet("/drafts", async (WhatsAppService service, CancellationToken ct, WhatsAppDraftStatus? status = null) =>
            Results.Ok(await service.ListDraftsAsync(status, ct)));

        // The only way a WhatsApp message becomes an entry.
        group.MapPost("/drafts/{id:guid}/confirm", async (Guid id, ConfirmWhatsAppDraftRequest request, WhatsAppService service, CancellationToken ct) =>
            Results.Ok(await service.ConfirmDraftAsync(id, request, ct)));

        group.MapPost("/drafts/{id:guid}/discard", async (Guid id, WhatsAppService service, CancellationToken ct) =>
        {
            await service.DiscardDraftAsync(id, ct);
            return Results.NoContent();
        });

        MapWebhook(app);
        return app;
    }

    /// <summary>Anonymous by design (Meta calls it); protected by the verify token and the app-secret signature.</summary>
    private static void MapWebhook(IEndpointRouteBuilder app)
    {
        var webhook = app.MapGroup("/webhooks/whatsapp").WithTags("WhatsApp").AllowAnonymous();

        webhook.MapGet("/", (HttpRequest request, IConfiguration config) =>
        {
            var expected = config["WhatsApp:VerifyToken"];
            var mode = request.Query["hub.mode"].ToString();
            var token = request.Query["hub.verify_token"].ToString();
            var challenge = request.Query["hub.challenge"].ToString();

            return !string.IsNullOrEmpty(expected) && mode == "subscribe" && FixedTimeEquals(token, expected)
                ? Results.Text(challenge)
                : Results.StatusCode(StatusCodes.Status403Forbidden);
        });

        webhook.MapPost("/", async (
            HttpRequest request,
            IConfiguration config,
            IHostEnvironment env,
            WhatsAppService service,
            ILoggerFactory loggers,
            CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger("WhatsAppWebhook");
            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(ct);

            var secret = config["WhatsApp:AppSecret"];
            if (string.IsNullOrEmpty(secret))
            {
                // Without an app secret the signature cannot be checked: only accepted in development.
                if (!env.IsDevelopment())
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            else if (!IsValidSignature(body, secret, request.Headers["X-Hub-Signature-256"].ToString()))
            {
                return Results.Unauthorized();
            }

            try
            {
                var replies = await service.HandleWebhookAsync(body, ct);
                // Outbound delivery through the Cloud API is a stub: replies are only logged for now.
                foreach (var reply in replies)
                    logger.LogInformation("WhatsApp reply queued for {Phone}", reply.PhoneE164);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Always acknowledge so Meta does not retry endlessly.
                logger.LogError(ex, "Falha ao processar webhook do WhatsApp");
            }

            return Results.Ok();
        });
    }

    private static bool IsValidSignature(string body, string secret, string header)
    {
        const string prefix = "sha256=";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));
        return FixedTimeEquals(header[prefix.Length..].ToUpperInvariant(), expected);
    }

    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
