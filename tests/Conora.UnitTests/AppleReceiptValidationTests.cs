using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Conora.Domain.Ports;
using Conora.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Conora.UnitTests;

public sealed class AppleReceiptValidationTests : IDisposable
{
    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), $"apple-test-{Guid.NewGuid():N}.p8");

    public AppleReceiptValidationTests()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(_keyPath, ecdsa.ExportPkcs8PrivateKeyPem());
    }

    public void Dispose() => File.Delete(_keyPath);

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)] // production before the first release
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Sandbox_purchase_is_confirmed_when_production_fails(HttpStatusCode productionStatus)
    {
        var handler = new AppleHandler(productionStatus, HttpStatusCode.OK);
        var receipt = await Validator(handler).ValidateAsync(
            StoreBillingChannel.Apple, "conora.individual.monthly", "2000001", "");

        Assert.Equal("conora.individual.monthly", receipt.ProductId);
        Assert.True(receipt.Entitled);
        Assert.Equal(2, handler.Hosts.Count);
        Assert.Contains("sandbox", handler.Hosts[1]);
    }

    [Fact]
    public async Task Purchase_is_rejected_when_both_environments_fail()
    {
        var handler = new AppleHandler(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Validator(handler).ValidateAsync(
            StoreBillingChannel.Apple, "conora.individual.monthly", "2000001", ""));

        Assert.Equal("A App Store não confirmou esta compra.", error.Message);
    }

    private StoreReceiptValidator Validator(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new StoreBillingOptions
        {
            AppleIssuerId = "issuer",
            AppleKeyId = "KEYID",
            ApplePrivateKeyPath = _keyPath,
        }),
        NullLogger<StoreReceiptValidator>.Instance);

    private sealed class AppleHandler(HttpStatusCode production, HttpStatusCode sandbox) : HttpMessageHandler
    {
        public List<string> Hosts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var host = request.RequestUri!.Host;
            Hosts.Add(host);
            var status = host.Contains("sandbox") ? sandbox : production;
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.OK)
            {
                var body = JsonSerializer.Serialize(new { signedTransactionInfo = SignedTransaction() });
                response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }
            return Task.FromResult(response);
        }

        private static string SignedTransaction()
        {
            static string B64(object value) =>
                Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var payload = new
            {
                environment = "Sandbox",
                bundleId = "br.com.conora.app",
                productId = "conora.individual.monthly",
                transactionId = "2000001",
                originalTransactionId = "2000001",
                expiresDate = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds(),
            };
            return $"{B64(new { alg = "ES256" })}.{B64(payload)}.sig";
        }
    }
}
