using System.Text;
using System.Text.Json;
using Conora.Domain.Ports;
using Microsoft.Extensions.Options;

namespace Conora.Infrastructure.Billing;

/// <summary>
/// Turns the stores' native server notifications into <see cref="StoreNoticeCommand"/>.
/// A null result means the notification is authentic but carries nothing to apply.
/// Invalid or unauthenticated payloads throw <see cref="StoreNotificationRejectedException"/>.
/// </summary>
public interface IStoreNotificationReader
{
    /// <summary>App Store Server Notifications V2 (<c>{ "signedPayload": "&lt;JWS&gt;" }</c>).</summary>
    StoreNoticeCommand? ReadApple(string? signedPayload);

    /// <summary>Google Play RTDN delivered by a Pub/Sub push (base64 <c>message.data</c>).</summary>
    Task<StoreNoticeCommand?> ReadGoogleAsync(string? messageData, CancellationToken ct = default);
}

/// <summary>Ported from Clampfy StoreNotificationReader; the receipt state (not the event name) drives the plan.</summary>
public sealed class StoreNotificationReader : IStoreNotificationReader
{
    private readonly IStoreReceiptValidator _validator;
    private readonly StoreBillingOptions _store;
    private readonly AppleSignedDataVerifier _apple;

    public StoreNotificationReader(IStoreReceiptValidator validator, IOptions<StoreBillingOptions> store)
    {
        _validator = validator;
        _store = store.Value;
        _apple = new AppleSignedDataVerifier(_store.AppleRootCertificateSha256);
    }

    public StoreNoticeCommand? ReadApple(string? signedPayload)
    {
        using var notification = _apple.Verify(signedPayload);
        var root = notification.RootElement;
        var type = StoreReceiptValidator.GetString(root, "notificationType");
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return null; // TEST and summary notifications carry no transaction data.

        if (!string.Equals(StoreReceiptValidator.GetString(data, "bundleId"), _store.BundleId, StringComparison.Ordinal))
            throw new StoreNotificationRejectedException("App Store notification for another bundle.");
        if (type is null || !RelevantAppleTypes.Contains(type))
            return null;

        var signedTransaction = StoreReceiptValidator.GetString(data, "signedTransactionInfo");
        if (string.IsNullOrWhiteSpace(signedTransaction))
            return null;
        using var transactionDoc = _apple.Verify(signedTransaction);
        StoreReceipt receipt;
        try
        {
            receipt = StoreReceiptValidator.ReadAppleTransaction(transactionDoc.RootElement, _store.BundleId, null, DateTime.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new StoreNotificationRejectedException(ex.Message);
        }
        return new StoreNoticeCommand(StoreBillingChannel.Apple, type, receipt);
    }

    public async Task<StoreNoticeCommand?> ReadGoogleAsync(string? messageData, CancellationToken ct = default)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(messageData ?? "")));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new StoreNotificationRejectedException("Malformed Google Play notification.");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new StoreNotificationRejectedException("Malformed Google Play notification.");
            if (!string.Equals(StoreReceiptValidator.GetString(root, "packageName"), _store.PackageName, StringComparison.Ordinal))
                throw new StoreNotificationRejectedException("Google Play notification for another package.");

            string evento;
            string? purchaseToken;
            if (root.TryGetProperty("subscriptionNotification", out var sub))
            {
                var type = sub.TryGetProperty("notificationType", out var t) && t.TryGetInt32(out var n) ? n : 0;
                evento = $"subscription:{type}";
                purchaseToken = StoreReceiptValidator.GetString(sub, "purchaseToken");
            }
            else if (root.TryGetProperty("voidedPurchaseNotification", out var voided))
            {
                // productType 1 = subscription; one-time products are not sold.
                var productType = voided.TryGetProperty("productType", out var pt) && pt.TryGetInt32(out var p) ? p : 0;
                if (productType != 1)
                    return null;
                evento = "voided";
                purchaseToken = StoreReceiptValidator.GetString(voided, "purchaseToken");
            }
            else
            {
                return null; // testNotification, oneTimeProductNotification...
            }

            if (string.IsNullOrWhiteSpace(purchaseToken))
                throw new StoreNotificationRejectedException("Google Play notification without purchase token.");

            // RTDN only says "something changed": the state itself comes from the Play Developer API.
            var receipt = await _validator.FetchGoogleSubscriptionAsync(purchaseToken, ct);
            if (evento == "voided")
                receipt = receipt with { Entitled = false, ExpiresAt = DateTime.UtcNow };
            return new StoreNoticeCommand(StoreBillingChannel.Google, evento, receipt);
        }
    }

    // DID_FAIL_TO_RENEW is skipped: access simply lapses at expiresDate and EXPIRED / DID_RENEW follows.
    private static readonly HashSet<string> RelevantAppleTypes =
    [
        "SUBSCRIBED",
        "DID_RENEW",
        "DID_CHANGE_RENEWAL_PREF",
        "EXPIRED",
        "GRACE_PERIOD_EXPIRED",
        "REFUND",
        "REFUND_REVERSED",
        "REVOKE"
    ];
}
