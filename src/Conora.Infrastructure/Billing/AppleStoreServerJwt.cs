using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Conora.Infrastructure.Billing;

internal static class AppleStoreServerJwt
{
    public static string Create(string issuerId, string keyId, string privateKeyPath, string bundleId)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(File.ReadAllText(privateKeyPath));
        var key = new ECDsaSecurityKey(ecdsa) { KeyId = keyId };
        // The ECDsa instance is disposed on return; a cached signature provider would reuse it on the next call.
        var credentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuerId,
            "appstoreconnect-v1",
            [new Claim("bid", bundleId)],
            now,
            now.AddMinutes(20),
            credentials);
        token.Header["kid"] = keyId;
        token.Header["typ"] = "JWT";
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static JsonDocument ReadPayload(string jws)
    {
        var parts = jws.Split('.');
        if (parts.Length < 2)
            throw new InvalidOperationException("Transação da App Store inválida.");
        var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        return JsonDocument.Parse(json);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
