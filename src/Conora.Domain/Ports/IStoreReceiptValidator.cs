namespace Conora.Domain.Ports;

/// <summary>
/// Store-confirmed state of one subscription.
/// <see cref="SubscriptionId"/> is the stable key across renewals: Apple originalTransactionId,
/// Google purchase token. <see cref="Entitled"/> is true while the store grants access
/// (paid, intro offer or billing grace period) and <see cref="ExpiresAt"/> has not passed.
/// </summary>
public sealed record StoreReceipt(
    string ProductId,
    string TransactionId,
    string SubscriptionId,
    DateTime? ExpiresAt,
    bool Entitled,
    bool InTrial,
    Guid? AccountId);

/// <summary>Confirms Play / App Store purchases against the store APIs. Missing credentials fail closed.</summary>
public interface IStoreReceiptValidator
{
    Task<StoreReceipt> ValidateAsync(
        StoreBillingChannel channel,
        string productId,
        string transactionId,
        string purchaseToken,
        CancellationToken ct = default);

    /// <summary>Reads the current Google Play subscription state by purchase token (used by RTDN).</summary>
    Task<StoreReceipt> FetchGoogleSubscriptionAsync(string purchaseToken, CancellationToken ct = default);
}
