using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users").WithTags("Users");

        group.MapPost("/", async (CreateUserRequest request, UserService service, CancellationToken ct) =>
        {
            var user = await service.CreateAsync(request, ct);
            return Results.Created($"/users/{user.Id}", user);
        });

        group.MapGet("/{id:guid}", async (Guid id, UserService service, CancellationToken ct) =>
        {
            var user = await service.GetByIdAsync(id, ct);
            return Results.Ok(user);
        });

        return app;
    }
}
