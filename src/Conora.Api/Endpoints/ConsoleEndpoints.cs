using Conora.Domain.Catalog;
using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class ConsoleEndpoints
{
    public static IEndpointRouteBuilder MapConsoleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/console/kpis", async (HttpContext http, ConsoleService service, CancellationToken ct) =>
        {
            var email = http.User.FindFirst("email")?.Value;
            if (!ConsoleAccess.IsAllowed(email))
                return Results.Json(new { title = "Acesso negado" }, statusCode: StatusCodes.Status403Forbidden);

            return Results.Ok(await service.GetAsync(ct));
        }).WithTags("Console").RequireAuthorization();

        return app;
    }
}
