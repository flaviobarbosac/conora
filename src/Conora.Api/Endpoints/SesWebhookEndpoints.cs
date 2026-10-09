using System.Text;
using Conora.Services;

namespace Conora.Api.Endpoints;

public static class SesWebhookEndpoints
{
    public static IEndpointRouteBuilder MapSesWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var webhook = app.MapGroup("/webhooks/ses").WithTags("SES").AllowAnonymous();

        webhook.MapPost("/", async (
            HttpRequest request,
            SesFeedbackService service,
            ILoggerFactory loggers,
            CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger("SesWebhook");
            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(ct);

            try
            {
                await service.HandleSnsPayloadAsync(body, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Acknowledge so SNS does not retry endlessly on poison payloads.
                logger.LogError(ex, "Falha ao processar webhook SES/SNS");
            }

            return Results.Ok();
        });

        return app;
    }
}
