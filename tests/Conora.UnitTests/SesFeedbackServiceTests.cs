using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Conora.Domain.Ports;
using Conora.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Conora.UnitTests;

public class SesFeedbackServiceTests
{
    private const string Topic = "arn:aws:sns:sa-east-1:111111111111:ses-feedback";
    private const string CertUrl = "https://sns.sa-east-1.amazonaws.com/cert.pem";
    private const string SubscribeUrl = "https://sns.sa-east-1.amazonaws.com/?Action=ConfirmSubscription";
    private const string Bounce =
        """{"notificationType":"Bounce","bounce":{"bouncedRecipients":[{"emailAddress":"a@example.com"}]},"mail":{"messageId":"m1"}}""";

    private readonly RSA _key = RSA.Create(2048);
    private readonly FakeHandler _http = new();
    private readonly FakeSuppressions _suppressions = new();
    private readonly SesFeedbackService _service;

    public SesFeedbackServiceTests()
    {
        _http.Certificate = BuildCertificatePem(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var factory = new FakeFactory(_http);
        var verifier = new SnsMessageVerifier(
            factory,
            Options.Create(new SesFeedbackOptions { AllowedTopicArns = [Topic] }),
            NullLogger<SnsMessageVerifier>.Instance);
        _service = new SesFeedbackService(_suppressions, factory, verifier, NullLogger<SesFeedbackService>.Instance);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public async Task Signed_bounce_from_our_topic_suppresses_the_address(string signatureVersion)
    {
        var accepted = await _service.HandleSnsPayloadAsync(Signed(Notification(), signatureVersion), default);

        Assert.True(accepted);
        Assert.Equal(["a@example.com"], _suppressions.Emails);
    }

    [Fact]
    public async Task Signed_notification_with_subject_is_accepted()
    {
        var message = Notification();
        message["Subject"] = "Amazon SES Email Event Notification";

        Assert.True(await _service.HandleSnsPayloadAsync(Signed(message), default));
    }

    [Fact]
    public async Task Tampered_message_is_rejected()
    {
        var message = JsonSerializer.Deserialize<Dictionary<string, string>>(Signed(Notification()))!;
        message["Message"] = Bounce.Replace("a@example.com", "victim@example.com");

        var accepted = await _service.HandleSnsPayloadAsync(JsonSerializer.Serialize(message), default);

        Assert.False(accepted);
        Assert.Empty(_suppressions.Emails);
    }

    [Fact]
    public async Task Unsigned_forged_bounce_is_rejected_without_any_request()
    {
        var accepted = await _service.HandleSnsPayloadAsync(JsonSerializer.Serialize(Notification()), default);

        Assert.False(accepted);
        Assert.Empty(_suppressions.Emails);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Validly_signed_message_from_another_topic_is_rejected()
    {
        var message = Notification();
        message["TopicArn"] = "arn:aws:sns:sa-east-1:222222222222:attacker";

        var accepted = await _service.HandleSnsPayloadAsync(Signed(message), default);

        Assert.False(accepted);
        Assert.Empty(_suppressions.Emails);
        Assert.Empty(_http.Requests);
    }

    [Theory]
    [InlineData("https://attacker.example.com/cert.pem")]
    [InlineData("https://sns.sa-east-1.amazonaws.com.attacker.example/cert.pem")]
    [InlineData("http://sns.sa-east-1.amazonaws.com/cert.pem")]
    [InlineData("https://sns.sa-east-1.amazonaws.com:8443/cert.pem")]
    [InlineData("https://sns.sa-east-1.amazonaws.com/cert.txt")]
    public async Task Certificate_outside_sns_is_never_downloaded(string certificateUrl)
    {
        var message = Notification();
        message["SigningCertURL"] = certificateUrl;

        var accepted = await _service.HandleSnsPayloadAsync(Signed(message), default);

        Assert.False(accepted);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Expired_certificate_is_rejected()
    {
        _http.Certificate = BuildCertificatePem(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));

        Assert.False(await _service.HandleSnsPayloadAsync(Signed(Notification()), default));
        Assert.Empty(_suppressions.Emails);
    }

    [Fact]
    public async Task Certificate_download_failure_propagates_so_sns_can_retry()
    {
        _http.CertificateStatus = HttpStatusCode.ServiceUnavailable;

        await Assert.ThrowsAsync<HttpRequestException>(
            () => _service.HandleSnsPayloadAsync(Signed(Notification()), default));
        Assert.Empty(_suppressions.Emails);
    }

    [Fact]
    public async Task Certificate_is_downloaded_once()
    {
        await _service.HandleSnsPayloadAsync(Signed(Notification()), default);
        await _service.HandleSnsPayloadAsync(Signed(Notification()), default);

        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task Signed_confirmation_calls_the_sns_subscribe_url()
    {
        Assert.True(await _service.HandleSnsPayloadAsync(Signed(Confirmation(SubscribeUrl)), default));

        Assert.Contains(SubscribeUrl, _http.Requests);
    }

    [Fact]
    public async Task Forged_confirmation_never_calls_its_url()
    {
        const string target = "https://internal.example.com/admin";

        var accepted = await _service.HandleSnsPayloadAsync(JsonSerializer.Serialize(Confirmation(target)), default);

        Assert.False(accepted);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Signed_confirmation_with_a_foreign_url_does_not_call_it()
    {
        const string target = "https://internal.example.com/admin";

        Assert.True(await _service.HandleSnsPayloadAsync(Signed(Confirmation(target)), default));

        Assert.DoesNotContain(target, _http.Requests);
    }

    private static Dictionary<string, string> Notification() => new()
    {
        ["Type"] = "Notification",
        ["MessageId"] = "id-1",
        ["TopicArn"] = Topic,
        ["Message"] = Bounce,
        ["Timestamp"] = "2026-11-06T03:00:00.000Z",
        ["SigningCertURL"] = CertUrl,
    };

    private static Dictionary<string, string> Confirmation(string subscribeUrl) => new()
    {
        ["Type"] = "SubscriptionConfirmation",
        ["MessageId"] = "id-2",
        ["Token"] = "token",
        ["TopicArn"] = Topic,
        ["Message"] = "Confirm",
        ["SubscribeURL"] = subscribeUrl,
        ["Timestamp"] = "2026-11-06T03:00:00.000Z",
        ["SigningCertURL"] = CertUrl,
    };

    // Applies the SNS signing format independently of the production code.
    private string Signed(Dictionary<string, string> message, string signatureVersion = "2")
    {
        var fields = message["Type"] == "Notification"
            ? new[] { "Message", "MessageId", "Subject", "Timestamp", "TopicArn", "Type" }
            : ["Message", "MessageId", "SubscribeURL", "Timestamp", "Token", "TopicArn", "Type"];
        var text = new StringBuilder();
        foreach (var field in fields)
        {
            if (message.TryGetValue(field, out var value))
                text.Append(field).Append('\n').Append(value).Append('\n');
        }

        var hash = signatureVersion == "1" ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256;
        var signature = _key.SignData(Encoding.UTF8.GetBytes(text.ToString()), hash, RSASignaturePadding.Pkcs1);
        return JsonSerializer.Serialize(new Dictionary<string, string>(message)
        {
            ["SignatureVersion"] = signatureVersion,
            ["Signature"] = Convert.ToBase64String(signature),
        });
    }

    private string BuildCertificatePem(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        var request = new CertificateRequest("CN=sns.test", _key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        return certificate.ExportCertificatePem();
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public string Certificate { get; set; } = "";
        public HttpStatusCode CertificateStatus { get; set; } = HttpStatusCode.OK;
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            return Task.FromResult(url == CertUrl
                ? new HttpResponseMessage(CertificateStatus) { Content = new StringContent(Certificate) }
                : new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeSuppressions : IEmailSuppressionStore
    {
        public List<string> Emails { get; } = [];

        public Task<bool> IsSuppressedAsync(string email, CancellationToken ct = default) =>
            Task.FromResult(Emails.Contains(email));

        public Task UpsertAsync(string email, string reason, string? sourceMessageId, CancellationToken ct = default)
        {
            Emails.Add(email);
            return Task.CompletedTask;
        }

        public Task RemoveUserOptOutAsync(string email, CancellationToken ct = default) => Task.CompletedTask;
    }
}
