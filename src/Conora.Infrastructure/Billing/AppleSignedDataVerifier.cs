using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Conora.Domain.Ports;

namespace Conora.Infrastructure.Billing;

/// <summary>
/// Verifies App Store signed data (JWS with an x5c chain) the same way Apple's
/// App Store Server Library does: ES256 signature by the leaf, chain built up to a
/// pinned root (Apple Root CA - G3 by default) and Apple's marker OIDs on leaf/intermediate.
/// </summary>
public sealed class AppleSignedDataVerifier
{
    /// <summary>SHA-256 of https://www.apple.com/certificateauthority/AppleRootCA-G3.cer.</summary>
    public const string AppleRootCaG3Sha256 = "63343ABFB89A6A03EBB57E9B3F5FA7BE7C4F5C756F3017B3A8C488C3653E9179";

    public const string LeafMarkerOid = "1.2.840.113635.100.6.11.1";
    public const string IntermediateMarkerOid = "1.2.840.113635.100.6.2.1";

    private readonly string _rootSha256;

    public AppleSignedDataVerifier(string? rootSha256 = null)
    {
        _rootSha256 = Normalize(string.IsNullOrWhiteSpace(rootSha256) ? AppleRootCaG3Sha256 : rootSha256);
    }

    /// <summary>Returns the verified JWS payload. Throws <see cref="StoreNotificationRejectedException"/> otherwise.</summary>
    public JsonDocument Verify(string? jws)
    {
        var parts = (jws ?? "").Split('.');
        if (parts.Length != 3 || parts.Any(string.IsNullOrEmpty))
            throw new StoreNotificationRejectedException("Malformed App Store JWS.");

        byte[] signature;
        List<X509Certificate2> certs;
        try
        {
            using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]));
            var alg = header.RootElement.TryGetProperty("alg", out var algEl) ? algEl.GetString() : null;
            if (alg != "ES256")
                throw new StoreNotificationRejectedException("Unexpected App Store JWS algorithm.");
            if (!header.RootElement.TryGetProperty("x5c", out var x5c) || x5c.ValueKind != JsonValueKind.Array)
                throw new StoreNotificationRejectedException("App Store JWS without certificate chain.");
            certs = x5c.EnumerateArray()
                .Select(c => X509CertificateLoader.LoadCertificate(Convert.FromBase64String(c.GetString() ?? "")))
                .ToList();
            signature = Base64Url.DecodeFromChars(parts[2]);
        }
        catch (StoreNotificationRejectedException)
        {
            throw;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or InvalidOperationException)
        {
            throw new StoreNotificationRejectedException("Malformed App Store JWS header.");
        }

        try
        {
            VerifyChain(certs);
            using var key = certs[0].GetECDsaPublicKey()
                ?? throw new StoreNotificationRejectedException("App Store leaf certificate is not EC.");
            var signedBytes = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            if (!key.VerifyData(signedBytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new StoreNotificationRejectedException("Invalid App Store JWS signature.");

            try
            {
                return JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                throw new StoreNotificationRejectedException("Malformed App Store JWS payload.");
            }
        }
        finally
        {
            foreach (var cert in certs)
                cert.Dispose();
        }
    }

    private void VerifyChain(IReadOnlyList<X509Certificate2> certs)
    {
        if (certs.Count < 3)
            throw new StoreNotificationRejectedException("Incomplete App Store certificate chain.");

        var root = certs[^1];
        if (Normalize(root.GetCertHashString(HashAlgorithmName.SHA256)) != _rootSha256)
            throw new StoreNotificationRejectedException("Untrusted App Store root certificate.");
        if (certs[0].Extensions[LeafMarkerOid] is null)
            throw new StoreNotificationRejectedException("App Store leaf certificate is missing Apple's marker.");
        if (certs[1].Extensions[IntermediateMarkerOid] is null)
            throw new StoreNotificationRejectedException("App Store intermediate certificate is missing Apple's marker.");

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        for (var i = 1; i < certs.Count - 1; i++)
            chain.ChainPolicy.ExtraStore.Add(certs[i]);

        if (!chain.Build(certs[0]) || chain.ChainElements.Count != certs.Count)
            throw new StoreNotificationRejectedException("Invalid App Store certificate chain.");
        for (var i = 0; i < certs.Count; i++)
        {
            if (!string.Equals(chain.ChainElements[i].Certificate.Thumbprint, certs[i].Thumbprint, StringComparison.OrdinalIgnoreCase))
                throw new StoreNotificationRejectedException("Unexpected App Store certificate chain order.");
        }
    }

    private static string Normalize(string hex) =>
        hex.Replace(":", "").Replace(" ", "").ToUpperInvariant();
}
