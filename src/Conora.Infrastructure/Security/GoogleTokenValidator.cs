using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;

namespace Conora.Infrastructure.Security;

public sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    private readonly string? _clientId;

    public GoogleTokenValidator(IConfiguration configuration)
    {
        _clientId = configuration["Google:ClientId"];
    }

    public async Task<GoogleIdentity> ValidateAsync(string idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_clientId))
            throw new GoogleAuthDisabledException();

        var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = [_clientId]
        });

        if (string.IsNullOrWhiteSpace(payload.Email) || !payload.EmailVerified)
            throw new InvalidTokenException();

        return new GoogleIdentity(
            payload.Subject,
            payload.Email.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(payload.Name) ? payload.Email : payload.Name,
            true);
    }
}
