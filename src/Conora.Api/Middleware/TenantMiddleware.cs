using Conora.Domain.Ports;

namespace Conora.Api.Middleware;

public sealed class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, ITenantContext tenant)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var raw = user.FindFirst("sub")?.Value
                      ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(raw, out var usuarioId))
                tenant.Set(usuarioId);
        }

        await _next(context);
    }
}
