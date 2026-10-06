using Conora.Services;

namespace Conora.Api.Endpoints;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users").WithTags("Users").RequireAuthorization();

        group.MapGet("/{id:guid}", async (Guid id, UserService service, CancellationToken ct) =>
        {
            var user = await service.GetByIdAsync(id, ct);
            return Results.Ok(user);
        });

        return app;
    }
}
