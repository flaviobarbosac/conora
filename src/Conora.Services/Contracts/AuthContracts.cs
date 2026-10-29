namespace Conora.Services.Contracts;

public sealed record RegisterRequest(string Name, string Email, string Cpf, string Password);
public sealed record LoginRequest(string Password, string? Usuario = null, string? Email = null);
/// <param name="Intent">"login" (default) or "register". Login never creates; register never signs into an existing id.</param>
public sealed record GoogleLoginRequest(string IdToken, string? Intent = null);
/// <param name="Intent">"login" (default) or "register". Login never creates; register never signs into an existing id.</param>
public sealed record AppleLoginRequest(string IdentityToken, string? Name = null, string? Email = null, string? Intent = null);
public sealed record RefreshRequest(string RefreshToken);
public sealed record ForgotPasswordRequest(string Usuario);
public sealed record ResetPasswordRequest(string Token, string Password);
public sealed record VerifyEmailRequest(string Token);
public sealed record AuthResponse(Guid UserId, string Email, string AccessToken, string RefreshToken, DateTime AccessExpiresAtUtc);
