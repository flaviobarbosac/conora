using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Services;

namespace Conora.Api.Middleware;

/// <summary>
/// After day 100 without a plan the account is blocked: only auth, the plans screen,
/// profile (app bootstrap), help/feedback and the LGPD deletion request stay reachable.
/// A Hotmart cancellation keeps two days for export, then the same block.
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

    private static readonly string[] AllowedWhenExportOnly =
    [
        "/auth",
        "/family",
        "/me",
        "/plan",
        "/gamification",
        "/help",
        "/feedback",
        "/webhooks",
        "/console",
        "/users"
    ];

    private readonly RequestDelegate _next;

    public PlanAccessMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, ITenantContext tenant, PlanService plan)
    {
        if (tenant.UsuarioId is not null)
        {
            var access = await plan.GetAccessAsync(context.RequestAborted);
            var path = context.Request.Path;
            var exportOnly = access.ExportOnlyUntil is DateTime until && until > DateTime.UtcNow;
            if (exportOnly)
            {
                if (!IsAllowed(path, AllowedWhenExportOnly))
                    throw new PlanBlockedException();
            }
            else if (access.State.IsBlocked && !IsAllowed(path, AllowedWhenBlocked))
            {
                throw new PlanBlockedException();
            }
        }

        await _next(context);
    }

    private static bool IsAllowed(PathString path, string[] prefixes)
        => prefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}
