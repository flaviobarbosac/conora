using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Conora.Domain.Ports;
using Conora.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Conora.IntegrationTests;

public class AppleTokenRevokerTests
{
    [Fact]
    public void Client_secret_is_an_es256_jwt_apple_accepts()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = key.ExportPkcs8PrivateKeyPem();
        var now = DateTime.UtcNow;

        var secret = AppleTokenRevoker.CreateClientSecret("N2JF8644LJ", "ABC123DEFG", pem, "br.com.conora.app", now);

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        handler.ValidateToken(secret, new TokenValidationParameters
        {
            ValidIssuer = "N2JF8644LJ",
            ValidAudience = "https://appleid.apple.com",
            IssuerSigningKey = new ECDsaSecurityKey(ECDsa.Create(key.ExportParameters(false))),
            ValidateLifetime = true
        }, out var validated);

        var jwt = (JwtSecurityToken)validated;
        Assert.Equal("ES256", jwt.Header.Alg);
        Assert.Equal("ABC123DEFG", jwt.Header.Kid);
        Assert.Equal("br.com.conora.app", jwt.Subject);
        Assert.True(jwt.ValidTo <= now.AddMinutes(6));
    }

    [Fact]
    public void Private_key_is_accepted_without_line_breaks()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = key.ExportPkcs8PrivateKeyPem();
        var singleLine = pem.Replace("\r", string.Empty).Replace("\n", string.Empty);

        Assert.Equal(key.ExportPkcs8PrivateKey(), AppleTokenRevoker.DecodePrivateKey(singleLine));
    }

    [Fact]
    public async Task Revocation_is_skipped_when_the_key_is_not_configured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Apple:ClientId"] = "br.com.conora.app" })
            .Build();
        var revoker = new AppleTokenRevoker(configuration, new NoHttpClientFactory(), NullLogger<AppleTokenRevoker>.Instance);

        Assert.False(await revoker.RevokeAsync("code", AppleClientKind.Ios, null));
    }

    private sealed class NoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No HTTP call expected.");
    }
}
