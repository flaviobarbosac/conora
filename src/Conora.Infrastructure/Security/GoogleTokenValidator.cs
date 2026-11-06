using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;

namespace Conora.Infrastructure.Security;

public sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

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

        // Web and Android ID tokens carry the web client id; the iOS SDK signs with the iOS client id.
        // The web account picker returns an OAuth access token (not a JWT). Native sign-in still sends an ID token.
        var audiences = new[] { _clientId, _iosClientId }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (idToken.Count(static c => c == '.') != 2)
            return await ValidateAccessTokenAsync(idToken, audiences, ct);

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

    private static async Task<GoogleIdentity> ValidateAccessTokenAsync(
        string accessToken,
        string[] audiences,
        CancellationToken ct)
    {
        TokenInfo? info;
        try
        {
            info = await Http.GetFromJsonAsync<TokenInfo>(
                "https://oauth2.googleapis.com/tokeninfo?access_token=" + Uri.EscapeDataString(accessToken),
                ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new InvalidTokenException();
        }

        var audience = First(info?.Aud, info?.Azp);
        if (info is null
            || string.IsNullOrWhiteSpace(info.Sub)
            || string.IsNullOrWhiteSpace(info.Email)
            || !IsVerified(info.EmailVerified)
            || audience is null
            || !audiences.Contains(audience, StringComparer.Ordinal))
        {
            throw new InvalidTokenException();
        }

        return new GoogleIdentity(
            info.Sub,
            info.Email.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(info.Name) ? info.Email : info.Name,
            true);
    }

    private static string? First(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static bool IsVerified(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private sealed class TokenInfo
    {
        [JsonPropertyName("aud")]
        public string? Aud { get; set; }

        [JsonPropertyName("azp")]
        public string? Azp { get; set; }

        [JsonPropertyName("sub")]
        public string? Sub { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("email_verified")]
        public JsonElement EmailVerified { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
