namespace Conora.Services.Contracts;

public sealed record LgpdExportResponse(Guid UserId, string Email, string Name, DateTime ExportedAtUtc, object Data);

/// <summary>
/// Optional body of the account deletion. Users who signed in with Apple send a fresh authorization code
/// so the Sign in with Apple grant can be revoked. AppleClient is "ios" (native app) or "web".
/// </summary>
public sealed record DeleteAccountRequest(
    string? AppleAuthorizationCode = null,
    string? AppleClient = null,
    string? AppleRedirectUri = null);
