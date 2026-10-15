namespace Conora.Domain.Ports;

public sealed record AppleIdentity(string AppleId, string? Email, bool EmailVerified);

public interface IAppleTokenValidator
{
    Task<AppleIdentity> ValidateAsync(string identityToken, CancellationToken ct = default);
}
