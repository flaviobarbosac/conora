using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;

namespace Conora.Infrastructure.Security;

public sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    private readonly string? _clientId;
    private readonly string? _iosClientId;

    public GoogleTokenValidator(IConfiguration configuration)
    {
        _clientId = configuration["Google:ClientId"];
        _iosClientId = configuration["Google:IosClientId"];
    }

    public async Task<GoogleIdentity> ValidateAsync(string idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_clientId))
            throw new GoogleAuthDisabledException();

        // Web and Android tokens carry the web client id; the iOS SDK signs with the iOS client id.
        var audiences = new[] { _clientId, _iosClientId }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = audiences
            });
        }
        catch (InvalidJwtException)
        {
            throw new InvalidTokenException();
        }

        if (string.IsNullOrWhiteSpace(payload.Email) || !payload.EmailVerified)
            throw new InvalidTokenException();

        return new GoogleIdentity(
            payload.Subject,
            payload.Email.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(payload.Name) ? payload.Email : payload.Name,
            true);
    }
}
