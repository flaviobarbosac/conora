namespace Conora.Domain.Ports;

public sealed record GoogleIdentity(string GoogleId, string Email, string Name, bool EmailVerified);

public interface IGoogleTokenValidator
{
    Task<GoogleIdentity> ValidateAsync(string idToken, CancellationToken ct = default);
}
