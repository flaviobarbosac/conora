using Conora.Services;
using Conora.Services.Contracts;
using Microsoft.AspNetCore.RateLimiting;

namespace Conora.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth").RequireRateLimiting("auth");

        group.MapPost("/register", async (RegisterRequest request, AuthService service, CancellationToken ct) =>
        {
            var result = await service.RegisterAsync(request, ct);
            return Results.Created("/users/me", result);
        });

        group.MapPost("/login", async (LoginRequest request, AuthService service, CancellationToken ct) =>
            Results.Ok(await service.LoginAsync(request, ct)));

        group.MapPost("/google", async (GoogleLoginRequest request, AuthService service, CancellationToken ct) =>
            Results.Ok(await service.GoogleAsync(request, ct)));

        group.MapPost("/apple", async (AppleLoginRequest request, AuthService service, CancellationToken ct) =>
            Results.Ok(await service.AppleAsync(request, ct)));

        group.MapPost("/forgot-password", async (ForgotPasswordRequest request, AuthService service, CancellationToken ct) =>
        {
            await service.RequestPasswordResetAsync(request, ct);
            return Results.NoContent();
        });

        group.MapPost("/reset-password", async (ResetPasswordRequest request, AuthService service, CancellationToken ct) =>
        {
            await service.ResetPasswordAsync(request, ct);
            return Results.NoContent();
        });

        group.MapPost("/refresh", async (RefreshRequest request, AuthService service, CancellationToken ct) =>
            Results.Ok(await service.RefreshAsync(request, ct)));

        group.MapPost("/logout", async (AuthService service, CancellationToken ct) =>
        {
            await service.LogoutAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        return app;
    }
}
