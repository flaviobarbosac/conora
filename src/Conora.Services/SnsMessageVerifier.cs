using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conora.Services;

public sealed class SesFeedbackOptions
{
    public const string SectionName = "SesFeedback";

    /// <summary>SNS topics allowed to call the webhook. Empty rejects every message.</summary>
    public string[] AllowedTopicArns { get; set; } = [];
}

/// <summary>
/// Checks that an SNS HTTP(S) message was signed by AWS and comes from one of our topics.
/// The topic check matters because anyone can create a topic and get validly signed messages.
/// </summary>
public sealed partial class SnsMessageVerifier
{
    private static readonly string[] NotificationFields = ["Message", "MessageId", "Subject", "Timestamp", "TopicArn", "Type"];
    private static readonly string[] ConfirmationFields = ["Message", "MessageId", "SubscribeURL", "Timestamp", "Token", "TopicArn", "Type"];

    private readonly ConcurrentDictionary<string, X509Certificate2> _certificates = new(StringComparer.Ordinal);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<SesFeedbackOptions> _options;
    private readonly ILogger<SnsMessageVerifier> _logger;

    public SnsMessageVerifier(
        IHttpClientFactory httpClientFactory,
        IOptions<SesFeedbackOptions> options,
        ILogger<SnsMessageVerifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    /// <summary>Returns false for messages that fail the checks; throws HttpRequestException when the certificate cannot be downloaded.</summary>
    public async Task<bool> IsAuthenticAsync(JsonElement message, CancellationToken ct)
    {
        var topicArn = ReadString(message, "TopicArn");
        if (topicArn is null || !_options.Value.AllowedTopicArns.Contains(topicArn, StringComparer.Ordinal))
            return Reject("topic not allowed");

        var certificateUrl = ReadString(message, "SigningCertURL");
        if (!IsSnsUrl(certificateUrl) || !certificateUrl!.EndsWith(".pem", StringComparison.Ordinal))
            return Reject("invalid certificate URL");

        var hash = ReadString(message, "SignatureVersion") switch
        {
            "1" => HashAlgorithmName.SHA1,
            "2" => HashAlgorithmName.SHA256,
            _ => default(HashAlgorithmName?),
        };
        if (hash is null)
            return Reject("unsupported signature version");

        var signedText = BuildSignedText(message);
        if (signedText is null)
            return Reject("unsupported message type or missing field");

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(ReadString(message, "Signature") ?? "");
        }
        catch (FormatException)
        {
            return Reject("malformed signature");
        }

        var certificate = await GetCertificateAsync(certificateUrl, ct);
        var now = DateTime.Now;
        if (now < certificate.NotBefore || now > certificate.NotAfter)
            return Reject("expired certificate");

        using var publicKey = certificate.GetRSAPublicKey();
        if (publicKey is null)
            return Reject("certificate has no RSA key");

        return publicKey.VerifyData(Encoding.UTF8.GetBytes(signedText), signature, hash.Value, RSASignaturePadding.Pkcs1)
            || Reject("signature mismatch");
    }

    public static bool IsSnsUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && SnsHost().IsMatch(uri.Host);

    private static string? BuildSignedText(JsonElement message)
    {
        var type = ReadString(message, "Type");
        var fields = type switch
        {
            "Notification" => NotificationFields,
            "SubscriptionConfirmation" or "UnsubscribeConfirmation" => ConfirmationFields,
            _ => null,
        };
        if (fields is null)
            return null;

        var text = new StringBuilder();
        foreach (var field in fields)
        {
            var value = ReadString(message, field);
            if (value is null)
            {
                // Subject is the only optional signed field.
                if (field == "Subject")
                    continue;
                return null;
            }

            text.Append(field).Append('\n').Append(value).Append('\n');
        }

        return text.ToString();
    }

    private async Task<X509Certificate2> GetCertificateAsync(string url, CancellationToken ct)
    {
        if (_certificates.TryGetValue(url, out var cached))
            return cached;

        var client = _httpClientFactory.CreateClient("ses-sns");
        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var pem = await response.Content.ReadAsStringAsync(ct);
        return _certificates.GetOrAdd(url, _ => X509Certificate2.CreateFromPem(pem));
    }

    private bool Reject(string reason)
    {
        _logger.LogWarning("SNS message rejected: {Reason}", reason);
        return false;
    }

    private static string? ReadString(JsonElement message, string name) =>
        message.ValueKind == JsonValueKind.Object
        && message.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    [GeneratedRegex(@"^sns\.[a-z0-9-]+\.amazonaws\.com$", RegexOptions.CultureInvariant)]
    private static partial Regex SnsHost();
}
