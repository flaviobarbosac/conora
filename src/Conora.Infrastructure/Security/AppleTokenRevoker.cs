using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Conora.Domain.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Conora.Infrastructure.Security;

/// <summary>
/// Sign in with Apple token revocation (https://developer.apple.com/documentation/sign_in_with_apple/revoke_tokens).
/// Needs Apple:TeamId, Apple:KeyId and Apple:PrivateKey (contents of the .p8 key with Sign in with Apple enabled).
/// </summary>
public sealed class AppleTokenRevoker : IAppleTokenRevoker
{
    private const string AppleAudience = "https://appleid.apple.com";
    private const string TokenUrl = "https://appleid.apple.com/auth/token";
    private const string RevokeUrl = "https://appleid.apple.com/auth/revoke";

    private readonly string? _teamId;
    private readonly string? _keyId;
    private readonly string? _privateKey;
    private readonly string? _bundleId;
    private readonly string? _servicesId;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AppleTokenRevoker> _logger;

    public AppleTokenRevoker(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<AppleTokenRevoker> logger)
    {
        _teamId = configuration["Apple:TeamId"]?.Trim();
        _keyId = configuration["Apple:KeyId"]?.Trim();
        _privateKey = configuration["Apple:PrivateKey"];
        _bundleId = configuration["Apple:ClientId"]?.Trim();
        _servicesId = configuration["Apple:ServicesId"]?.Trim();
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<bool> RevokeAsync(
        string authorizationCode,
        AppleClientKind client,
        string? redirectUri,
        CancellationToken ct = default)
    {
        var clientId = client == AppleClientKind.Ios ? _bundleId : _servicesId;
        if (string.IsNullOrWhiteSpace(authorizationCode))
            return false;
        if (string.IsNullOrWhiteSpace(_teamId) || string.IsNullOrWhiteSpace(_keyId)
            || string.IsNullOrWhiteSpace(_privateKey) || string.IsNullOrWhiteSpace(clientId))
        {
            _logger.LogWarning("Apple token revocation skipped: Apple:TeamId, Apple:KeyId or Apple:PrivateKey not configured");
            return false;
        }

        try
        {
            var clientSecret = CreateClientSecret(_teamId, _keyId, _privateKey, clientId, DateTime.UtcNow);
            var http = _httpClientFactory.CreateClient(nameof(AppleTokenRevoker));

            var tokenForm = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code"] = authorizationCode,
                ["grant_type"] = "authorization_code"
            };
            if (client == AppleClientKind.Web && !string.IsNullOrWhiteSpace(redirectUri))
                tokenForm["redirect_uri"] = redirectUri.Trim();

            using var tokenResponse = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(tokenForm), ct);
            var tokenJson = await tokenResponse.Content.ReadAsStringAsync(ct);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Apple code exchange failed ({Status}): {Body}", (int)tokenResponse.StatusCode, tokenJson);
                return false;
            }

            var (token, hint) = PickToken(tokenJson);
            if (token is null)
            {
                _logger.LogWarning("Apple code exchange returned no token to revoke");
                return false;
            }

            var revokeForm = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["token"] = token,
                ["token_type_hint"] = hint
            };
            using var revokeResponse = await http.PostAsync(RevokeUrl, new FormUrlEncodedContent(revokeForm), ct);
            if (!revokeResponse.IsSuccessStatusCode)
            {
                var body = await revokeResponse.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Apple token revocation failed ({Status}): {Body}", (int)revokeResponse.StatusCode, body);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Apple token revocation failed");
            return false;
        }
    }

    /// <summary>Builds the ES256 client secret JWT Apple expects on /auth/token and /auth/revoke.</summary>
    public static string CreateClientSecret(string teamId, string keyId, string privateKey, string clientId, DateTime nowUtc)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(DecodePrivateKey(privateKey), out _);

        var credentials = new SigningCredentials(new ECDsaSecurityKey(ecdsa) { KeyId = keyId }, SecurityAlgorithms.EcdsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };
        var token = new JwtSecurityToken(
            issuer: teamId,
            audience: AppleAudience,
            claims: [new Claim("sub", clientId)],
            notBefore: null,
            expires: nowUtc.AddMinutes(5),
            signingCredentials: credentials);
        token.Payload["iat"] = new DateTimeOffset(nowUtc).ToUnixTimeSeconds();

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Accepts the .p8 file contents with or without PEM armor and line breaks.</summary>
    public static byte[] DecodePrivateKey(string privateKey)
    {
        var body = privateKey
            .Replace("-----BEGIN PRIVATE KEY-----", string.Empty, StringComparison.Ordinal)
            .Replace("-----END PRIVATE KEY-----", string.Empty, StringComparison.Ordinal)
            .Replace("\\n", string.Empty, StringComparison.Ordinal);
        body = new string(body.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return Convert.FromBase64String(body);
    }

    private static (string? Token, string Hint) PickToken(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("refresh_token", out var refresh) && refresh.GetString() is { Length: > 0 } r)
            return (r, "refresh_token");
        if (doc.RootElement.TryGetProperty("access_token", out var access) && access.GetString() is { Length: > 0 } a)
            return (a, "access_token");
        return (null, string.Empty);
    }
}
