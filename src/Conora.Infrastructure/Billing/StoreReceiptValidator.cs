using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Conora.Domain.Ports;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conora.Infrastructure.Billing;

/// <summary>
/// Confirms a store purchase with Google Play (Developer API subscriptionsv2) or the App Store
/// (App Store Server API). Ported from Clampfy StoreReceiptValidator. Empty credentials fail closed.
/// </summary>
public sealed class StoreReceiptValidator : IStoreReceiptValidator
{
    private const string AppleProductionApi = "https://api.storekit.itunes.apple.com";
    private const string AppleSandboxApi = "https://api.storekit-sandbox.itunes.apple.com";
    private const string AndroidPublisherApi = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications";

    private readonly HttpClient _http;
    private readonly StoreBillingOptions _options;
    private readonly ILogger<StoreReceiptValidator> _logger;

    public StoreReceiptValidator(HttpClient http, IOptions<StoreBillingOptions> options, ILogger<StoreReceiptValidator> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StoreReceipt> ValidateAsync(
        StoreBillingChannel channel,
        string productId,
        string transactionId,
        string purchaseToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(transactionId))
            throw new InvalidOperationException("Recibo da loja incompleto.");

        return channel switch
        {
            StoreBillingChannel.Google => await ValidateGoogleAsync(productId, purchaseToken, ct),
            StoreBillingChannel.Apple => await ValidateAppleAsync(transactionId, ct),
            _ => throw new InvalidOperationException("Canal de loja inválido.")
        };
    }

    public async Task<StoreReceipt> FetchGoogleSubscriptionAsync(string purchaseToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(purchaseToken))
            throw new InvalidOperationException("Recibo da loja incompleto.");

        var accessToken = await GoogleAccessTokenAsync(ct);
        using var doc = await GetGoogleSubscriptionAsync(accessToken, purchaseToken, ct);
        return ReadGoogleSubscription(doc.RootElement, purchaseToken, null, DateTime.UtcNow);
    }

    private async Task<StoreReceipt> ValidateGoogleAsync(string productId, string purchaseToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(purchaseToken))
            throw new InvalidOperationException("Recibo da loja incompleto.");

        var accessToken = await GoogleAccessTokenAsync(ct);
        using var doc = await GetGoogleSubscriptionAsync(accessToken, purchaseToken, ct);
        var receipt = ReadGoogleSubscription(doc.RootElement, purchaseToken, productId, DateTime.UtcNow);

        // Play refunds subscriptions that are not acknowledged within 3 days.
        if (receipt.Entitled && IsAcknowledgementPending(doc.RootElement))
            await AcknowledgeGoogleAsync(accessToken, receipt.ProductId, purchaseToken, ct);
        return receipt;
    }

    /// <summary>
    /// Maps a Play Developer API <c>subscriptionsv2</c> resource to a receipt. The purchase token is the
    /// stable subscription id; access is granted while active, canceled-but-paid or in grace period
    /// and the line item has not expired.
    /// </summary>
    public static StoreReceipt ReadGoogleSubscription(JsonElement root, string purchaseToken, string? fallbackProductId, DateTime now)
    {
        var orderId = GetString(root, "latestOrderId");
        string? productId = null;
        DateTime? expiresAt = null;
        var fromOffer = false;
        if (root.TryGetProperty("lineItems", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                productId ??= GetString(item, "productId");
                if (DateTime.TryParse(GetString(item, "expiryTime"), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var expiry)
                    && (expiresAt is null || expiry > expiresAt))
                    expiresAt = expiry;
                if (item.TryGetProperty("offerDetails", out var offer)
                    && !string.IsNullOrWhiteSpace(GetString(offer, "offerId")))
                    fromOffer = true;
            }
        }
        productId ??= fallbackProductId;
        if (string.IsNullOrWhiteSpace(productId))
            throw new InvalidOperationException("A Google Play não devolveu o produto da assinatura.");

        var state = GetString(root, "subscriptionState") ?? "";
        var grantsAccess = state is "SUBSCRIPTION_STATE_ACTIVE"
            or "SUBSCRIPTION_STATE_IN_GRACE_PERIOD"
            or "SUBSCRIPTION_STATE_CANCELED";
        var entitled = grantsAccess && expiresAt is DateTime until && until > now;
        var transactionId = string.IsNullOrWhiteSpace(orderId) ? purchaseToken : orderId;
        // License-tester purchases carry a testPurchase object.
        var test = root.TryGetProperty("testPurchase", out _);
        return new StoreReceipt(productId, transactionId, purchaseToken, expiresAt, entitled, fromOffer, GoogleAccountId(root), test);
    }

    private static bool IsAcknowledgementPending(JsonElement root) =>
        string.Equals(GetString(root, "acknowledgementState"), "ACKNOWLEDGEMENT_STATE_PENDING", StringComparison.Ordinal);

    private async Task<string> GoogleAccessTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.GoogleServiceAccountJsonPath)
            || !File.Exists(_options.GoogleServiceAccountJsonPath))
            throw new InvalidOperationException("Validação da Google Play não está configurada.");

#pragma warning disable CS0618 // FromFile is the supported loader for service-account JSON in this package version.
        var credential = GoogleCredential.FromFile(_options.GoogleServiceAccountJsonPath)
            .CreateScoped("https://www.googleapis.com/auth/androidpublisher");
#pragma warning restore CS0618
        return await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);
    }

    private async Task<JsonDocument> GetGoogleSubscriptionAsync(string accessToken, string purchaseToken, CancellationToken ct)
    {
        var url = $"{AndroidPublisherApi}/{Uri.EscapeDataString(_options.PackageName)}/purchases/subscriptionsv2/tokens/{Uri.EscapeDataString(purchaseToken)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("A Google Play não confirmou esta compra.");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private async Task AcknowledgeGoogleAsync(string accessToken, string productId, string purchaseToken, CancellationToken ct)
    {
        var url = $"{AndroidPublisherApi}/{Uri.EscapeDataString(_options.PackageName)}/purchases/subscriptions/{Uri.EscapeDataString(productId)}/tokens/{Uri.EscapeDataString(purchaseToken)}:acknowledge";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent("{}") };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // The app plugin also acknowledges; a failure here is logged, not fatal.
            _logger.LogWarning("Google Play acknowledge failed for {ProductId}: {Status}", productId, (int)response.StatusCode);
        }
    }

    private static Guid? GoogleAccountId(JsonElement root)
    {
        if (root.TryGetProperty("externalAccountIdentifiers", out var ext)
            && Guid.TryParse(GetString(ext, "obfuscatedExternalAccountId"), out var parsed))
            return parsed;
        return null;
    }

    private async Task<StoreReceipt> ValidateAppleAsync(string transactionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.AppleIssuerId)
            || string.IsNullOrWhiteSpace(_options.AppleKeyId)
            || string.IsNullOrWhiteSpace(_options.ApplePrivateKeyPath)
            || !File.Exists(_options.ApplePrivateKeyPath))
            throw new InvalidOperationException("Validação da App Store não está configurada.");

        var jwt = AppleStoreServerJwt.Create(_options.AppleIssuerId, _options.AppleKeyId, _options.ApplePrivateKeyPath, _options.BundleId);
        // Production first; App Review and TestFlight purchases live in the sandbox.
        // Any production failure falls back to the sandbox: besides 404, production answers 401
        // while the app has never been released.
        var production = await GetAppleTransactionAsync(AppleProductionApi, jwt, transactionId, ct);
        if (production.Payload is not null
            && !string.Equals(AppleEnvironment(production.Payload), "Sandbox", StringComparison.OrdinalIgnoreCase))
        {
            using (production.Payload)
                return ReadAppleTransaction(production.Payload.RootElement, _options.BundleId, "Production", DateTime.UtcNow);
        }

        production.Payload?.Dispose();
        var sandbox = await GetAppleTransactionAsync(AppleSandboxApi, jwt, transactionId, ct);
        using (sandbox.Payload)
        {
            if (sandbox.Payload is null)
            {
                _logger.LogWarning(
                    "App Store transaction {TransactionId} not confirmed: production {ProductionStatus}, sandbox {SandboxStatus}",
                    transactionId, (int)production.StatusCode, (int)sandbox.StatusCode);
                throw new InvalidOperationException("A App Store não confirmou esta compra.");
            }
            return ReadAppleTransaction(sandbox.Payload.RootElement, _options.BundleId, "Sandbox", DateTime.UtcNow);
        }
    }

    private async Task<AppleTransactionCall> GetAppleTransactionAsync(string host, string jwt, string transactionId, CancellationToken ct)
    {
        var url = $"{host}/inApps/v1/transactions/{Uri.EscapeDataString(transactionId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return new AppleTransactionCall(response.StatusCode, null);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var envelope = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var signed = GetString(envelope.RootElement, "signedTransactionInfo")
            ?? throw new InvalidOperationException("A App Store não devolveu a transação.");
        // Fetched over TLS from Apple with our own key, so the payload is read without re-verifying the chain.
        return new AppleTransactionCall(response.StatusCode, AppleStoreServerJwt.ReadPayload(signed));
    }

    /// <summary>
    /// Maps an App Store <c>JWSTransactionDecodedPayload</c> to a receipt. originalTransactionId is the
    /// stable subscription id; access is granted until expiresDate unless the transaction was revoked.
    /// </summary>
    public static StoreReceipt ReadAppleTransaction(JsonElement payload, string bundleId, string? expectedEnvironment, DateTime now)
    {
        if (expectedEnvironment is not null
            && !string.Equals(GetString(payload, "environment"), expectedEnvironment, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A transação da App Store não pertence a este ambiente.");
        if (!string.Equals(GetString(payload, "bundleId"), bundleId, StringComparison.Ordinal))
            throw new InvalidOperationException("A transação da App Store é de outro app.");

        var productId = GetString(payload, "productId")
            ?? throw new InvalidOperationException("A App Store não devolveu o produto.");
        var transactionId = GetString(payload, "transactionId")
            ?? throw new InvalidOperationException("A App Store não devolveu a transação.");
        var originalId = GetString(payload, "originalTransactionId") ?? transactionId;
        var expiresAt = GetMillis(payload, "expiresDate");
        var revoked = GetMillis(payload, "revocationDate") is not null;
        var inTrial = payload.TryGetProperty("offerType", out var offerEl) && offerEl.TryGetInt32(out var offer) && offer == 1;
        Guid? accountId = Guid.TryParse(GetString(payload, "appAccountToken"), out var parsed) ? parsed : null;
        var entitled = !revoked && expiresAt is DateTime until && until > now;
        var sandbox = string.Equals(GetString(payload, "environment"), "Sandbox", StringComparison.OrdinalIgnoreCase);
        return new StoreReceipt(productId, transactionId, originalId, revoked ? GetMillis(payload, "revocationDate") : expiresAt,
            entitled, inTrial, accountId, sandbox);
    }

    private static string? AppleEnvironment(JsonDocument? payload) =>
        payload is null ? null : GetString(payload.RootElement, "environment");

    private static DateTime? GetMillis(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
            : null;

    internal static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private readonly record struct AppleTransactionCall(HttpStatusCode StatusCode, JsonDocument? Payload);
}
