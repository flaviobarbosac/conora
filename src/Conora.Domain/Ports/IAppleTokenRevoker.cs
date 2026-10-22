namespace Conora.Domain.Ports;

/// <summary>Which Apple client issued the authorization code: the iOS app (bundle id) or the website (Services ID).</summary>
public enum AppleClientKind
{
    Ios,
    Web
}

/// <summary>
/// Revokes a user's Sign in with Apple grant, as Apple requires when the account is deleted.
/// Best effort: failures are logged and never block the deletion.
/// </summary>
public interface IAppleTokenRevoker
{
    /// <summary>Exchanges a fresh authorization code for tokens and revokes them. Returns true when Apple confirmed the revocation.</summary>
    Task<bool> RevokeAsync(string authorizationCode, AppleClientKind client, string? redirectUri, CancellationToken ct = default);
}
