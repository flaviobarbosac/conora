using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Conora.Infrastructure.Security;

public sealed class AppleTokenValidator : IAppleTokenValidator
{
    private const string AppleIssuer = "https://appleid.apple.com";
    private const string AppleKeysUrl = "https://appleid.apple.com/auth/keys";

    private readonly string? _clientId;
    private readonly string? _servicesId;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AppleTokenValidator> _logger;
    private static readonly SemaphoreSlim KeysLock = new(1, 1);
    private static JsonWebKeySet? _cachedKeys;
    private static DateTime _keysFetchedUtc = DateTime.MinValue;

    public AppleTokenValidator(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<AppleTokenValidator> logger)
    {
        _clientId = configuration["Apple:ClientId"];
        _servicesId = configuration["Apple:ServicesId"];
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<AppleIdentity> ValidateAsync(string identityToken, CancellationToken ct = default)
    {
        var audiences = Audiences();
        if (audiences.Length == 0)
            throw new AppleAuthDisabledException();
        if (string.IsNullOrWhiteSpace(identityToken))
            throw new InvalidTokenException();

        var keys = await GetKeysAsync(ct);
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = AppleIssuer,
            ValidAudiences = audiences,
            IssuerSigningKeys = keys.GetSigningKeys(),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        };

        ClaimsPrincipal principal;
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            principal = handler.ValidateToken(identityToken, parameters, out _);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao validar identity token da Apple");
            throw new InvalidTokenException();
        }

        var subject = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            throw new InvalidTokenException();

        var email = principal.FindFirst("email")?.Value;
        var verifiedClaim = principal.FindFirst("email_verified")?.Value;
        var emailVerified = string.IsNullOrWhiteSpace(verifiedClaim)
            || string.Equals(verifiedClaim, "true", StringComparison.OrdinalIgnoreCase)
            || verifiedClaim == "1";

        return new AppleIdentity(
            subject,
            string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
            emailVerified);
    }

    private string[] Audiences()
    {
        return new[] { _clientId, _servicesId }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<JsonWebKeySet> GetKeysAsync(CancellationToken ct)
    {
        if (_cachedKeys is not null && DateTime.UtcNow - _keysFetchedUtc < TimeSpan.FromHours(24))
            return _cachedKeys;

        await KeysLock.WaitAsync(ct);
        try
        {
            if (_cachedKeys is not null && DateTime.UtcNow - _keysFetchedUtc < TimeSpan.FromHours(24))
                return _cachedKeys;

            var http = _httpClientFactory.CreateClient(nameof(AppleTokenValidator));
            using var response = await http.GetAsync(AppleKeysUrl, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            _cachedKeys = new JsonWebKeySet(json);
            _keysFetchedUtc = DateTime.UtcNow;
            return _cachedKeys;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao obter chaves da Apple");
            if (_cachedKeys is not null)
                return _cachedKeys;
            throw new InvalidTokenException();
        }
        finally
        {
            KeysLock.Release();
        }
    }
}
