using Conora.Domain.Catalog;
using Conora.Domain.Enums;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class ConsoleEndpoints
{
    public static IEndpointRouteBuilder MapConsoleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/console/kpis", async (HttpContext http, ConsoleService service, CancellationToken ct) =>
        {
            if (!IsConsoleOperator(http))
                return Forbidden();

            return Results.Ok(await service.GetAsync(ct));
        }).WithTags("Console").RequireAuthorization();

        var feedback = app.MapGroup("/console/feedback").WithTags("Console").RequireAuthorization();

        feedback.MapGet("/", async (
            HttpContext http,
            FeedbackService service,
            Guid? usuarioId,
            FeedbackStatus? status,
            DateTime? from,
            DateTime? to,
            string? modulo,
            int? nota,
            int? notaMin,
            int? take,
            CancellationToken ct) =>
        {
            if (!IsConsoleOperator(http))
                return Forbidden();

            var items = await service.ListAdminAsync(
                usuarioId,
                status,
                NormalizeFrom(from),
                NormalizeTo(to),
                modulo,
                nota,
                notaMin,
                take ?? 200,
                ct);
            return Results.Ok(items);
        });

        feedback.MapGet("/usuarios", async (HttpContext http, FeedbackService service, CancellationToken ct) =>
        {
            if (!IsConsoleOperator(http))
                return Forbidden();

            return Results.Ok(await service.ListAdminUsuariosAsync(ct));
        });

        feedback.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            FeedbackService service,
            ITenantContext tenant,
            IUserRepository users,
            CancellationToken ct) =>
        {
            if (!IsConsoleOperator(http))
                return Forbidden();

            var admin = await ResolveAdminAsync(tenant, users, ct);
            if (admin is null)
                return Results.Unauthorized();

            var detail = await service.GetAdminDetailAsync(id, admin.Value.Id, admin.Value.Nome, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        feedback.MapPut("/{id:guid}/resposta", async (
            Guid id,
            ReplyFeedbackRequest request,
            HttpContext http,
            FeedbackService service,
            ITenantContext tenant,
            IUserRepository users,
            CancellationToken ct) =>
        {
            if (!IsConsoleOperator(http))
                return Forbidden();

            var admin = await ResolveAdminAsync(tenant, users, ct);
            if (admin is null)
                return Results.Unauthorized();

            var detail = await service.ReplyAdminAsync(id, admin.Value.Id, admin.Value.Nome, request, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        return app;
    }

    private static bool IsConsoleOperator(HttpContext http)
        => ConsoleAccess.IsAllowed(http.User.FindFirst("email")?.Value);

    private static IResult Forbidden()
        => Results.Json(new { title = "Acesso negado" }, statusCode: StatusCodes.Status403Forbidden);

    private static async Task<(Guid Id, string Nome)?> ResolveAdminAsync(
        ITenantContext tenant,
        IUserRepository users,
        CancellationToken ct)
    {
        if (tenant.UsuarioId is not Guid adminId)
            return null;

        var user = await users.GetByIdAsync(adminId, ct);
        if (user is null)
            return null;

        var nome = string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name;
        return (adminId, nome);
    }

    private static DateTime? NormalizeFrom(DateTime? value)
    {
        if (!value.HasValue) return null;
        var d = value.Value;
        if (d.Kind == DateTimeKind.Unspecified)
            return DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);
        return d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : d;
    }

    private static DateTime? NormalizeTo(DateTime? value)
    {
        if (!value.HasValue) return null;
        var d = value.Value;
        if (d.Kind == DateTimeKind.Unspecified)
        {
            var end = d.Date.AddDays(1).AddTicks(-1);
            return DateTime.SpecifyKind(end, DateTimeKind.Utc);
        }
        return d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : d;
    }
}
