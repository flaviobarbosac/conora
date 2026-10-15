using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Services;

namespace Conora.Api.Middleware;

/// <summary>
/// After day 100 without a plan the account is blocked: only auth, the plans screen,
/// profile (app bootstrap), help/feedback and the LGPD deletion request stay reachable.
/// Reading data or exporting from then on goes through support.
/// </summary>
public sealed class PlanAccessMiddleware
{
    private static readonly string[] AllowedWhenBlocked =
    [
        "/auth",
        "/plan",
        "/family/profile",
        "/help",
        "/feedback",
        "/me/delete",
        "/webhooks",
        "/console"
    ];

    private readonly RequestDelegate _next;

    public PlanAccessMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, ITenantContext tenant, PlanService plan)
    {
        if (tenant.UsuarioId is not null && !IsAllowedWhenBlocked(context.Request.Path))
        {
            var access = await plan.GetAccessAsync(context.RequestAborted);
            if (access.State.IsBlocked)
                throw new PlanBlockedException();
        }

        await _next(context);
    }

    private static bool IsAllowedWhenBlocked(PathString path)
        => AllowedWhenBlocked.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}
