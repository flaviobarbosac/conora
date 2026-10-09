namespace Conora.Services.Contracts;

public sealed record RegisterRequest(string Name, string Email, string Cpf, string Password);
public sealed record LoginRequest(string Password, string? Usuario = null, string? Email = null);
public sealed record GoogleLoginRequest(string IdToken);
public sealed record RefreshRequest(string RefreshToken);
public sealed record AuthResponse(Guid UserId, string Email, string AccessToken, string RefreshToken, DateTime AccessExpiresAtUtc);
