using Conora.Domain.Ports;
using Conora.Services;

namespace Conora.Api.Middleware;

/// <summary>
/// Headers X-Conora-Shared-Client and X-Conora-Month open the client tenant only when that month was shared.
/// X-Conora-Client stays the native platform marker and is not a tenant switch.
/// Without both share headers the request stays on the signed-in account.
/// </summary>
public sealed class ConsultantMonthMiddleware
{
    public const string ClientHeader = "X-Conora-Shared-Client";
    public const string MonthHeader = "X-Conora-Month";

    private readonly RequestDelegate _next;

    public ConsultantMonthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, ConsultantAccessService access)
    {
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/console", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/auth", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/months/shared-with-me", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var clientRaw = context.Request.Headers[ClientHeader].FirstOrDefault();
        var month = context.Request.Headers[MonthHeader].FirstOrDefault();
        var hasClient = !string.IsNullOrWhiteSpace(clientRaw);
        var hasMonth = !string.IsNullOrWhiteSpace(month);
        if (!hasClient && !hasMonth)
        {
            await _next(context);
            return;
        }

        // Expired or missing token must stay a 401 from authorization, not a 403 that the app treats as onboard.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        if (!hasClient || !hasMonth || !Guid.TryParse(clientRaw, out var clientId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { title = "Informe o cliente e o mês compartilhado." });
            return;
        }

        await access.EnterAsync(clientId, month!, context.RequestAborted);
        await _next(context);
    }
}
