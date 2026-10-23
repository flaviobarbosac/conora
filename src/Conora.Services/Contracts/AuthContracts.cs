namespace Conora.Services.Contracts;

public sealed record RegisterRequest(string Name, string Email, string Cpf, string Password);
public sealed record LoginRequest(string Password, string? Usuario = null, string? Email = null);
public sealed record GoogleLoginRequest(string IdToken);
public sealed record AppleLoginRequest(string IdentityToken, string? Name = null, string? Email = null);
public sealed record RefreshRequest(string RefreshToken);
public sealed record ForgotPasswordRequest(string Usuario);
public sealed record ResetPasswordRequest(string Token, string Password);
public sealed record VerifyEmailRequest(string Token);
public sealed record AuthResponse(Guid UserId, string Email, string AccessToken, string RefreshToken, DateTime AccessExpiresAtUtc);
