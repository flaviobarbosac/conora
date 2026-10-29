namespace Conora.Services.Contracts;

/// <summary>Acknowledgement that an export was queued for overnight processing.</summary>
public sealed record LgpdExportRequestResponse(
    Guid RequestId,
    DateTime RequestedAtUtc,
    string Message = "A exportação será agendada e estará disponível pra você daqui a 24 horas.");

public sealed record LgpdExportStatusResponse(
    bool ExportPending,
    bool ExportReady,
    bool CanDeleteAccount,
    DateTime? ExpiresAtUtc,
    string Message);

/// <summary>
/// Optional body of the account deletion. Users who signed in with Apple send a fresh authorization code
/// so the Sign in with Apple grant can be revoked. AppleClient is "ios" (native app) or "web".
/// </summary>
public sealed record DeleteAccountRequest(
    string? AppleAuthorizationCode = null,
    string? AppleClient = null,
    string? AppleRedirectUri = null);
