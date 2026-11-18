using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/push").WithTags("Push").RequireAuthorization();

        group.MapGet("/tokens", async (PushNotificationService service, CancellationToken ct) =>
            Results.Ok(await service.ListMineAsync(ct)));

        group.MapPost("/tokens", async (RegisterPushTokenRequest request, PushNotificationService service, CancellationToken ct) =>
            Results.Ok(await service.RegisterAsync(request, ct)));

        group.MapPost("/tokens/unregister", async (UnregisterPushTokenRequest request, PushNotificationService service, CancellationToken ct) =>
        {
            await service.UnregisterAsync(request, ct);
            return Results.NoContent();
        });

        // Smoke test: push to the caller's registered devices.
        group.MapPost("/test", async (SendPushRequest? request, PushNotificationService service, CancellationToken ct) =>
        {
            var body = request ?? new SendPushRequest("Conora", "Teste de push notification");
            if (string.IsNullOrWhiteSpace(body.Title))
                body = body with { Title = "Conora" };
            if (string.IsNullOrWhiteSpace(body.Body))
                body = body with { Body = "Teste de push notification" };
            return Results.Ok(await service.SendTestAsync(body, ct));
        });

        group.MapPost("/send", async (SendPushRequest request, PushNotificationService service, CancellationToken ct) =>
            Results.Ok(await service.SendAsync(request, ct)));

        group.MapGet("/status", (PushNotificationService service) =>
            Results.Ok(new { configured = service.IsConfigured }));

        return app;
    }
}
