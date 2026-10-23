namespace Conora.Domain.Ports;

public sealed record StoreReceipt(
    string ProductId,
    string TransactionId,
    string? SubscriptionId,
    bool Paid,
    bool InTrial,
    bool InGrace,
    decimal? Amount,
    Guid? AccountId);

/// <summary>
/// Confirms a Play / App Store purchase. Production implementation calls Google/Apple APIs;
/// local stub trusts the payload until credentials are configured.
/// </summary>
public interface IStoreReceiptValidator
{
    Task<StoreReceipt> ValidateAsync(
        StoreBillingChannel channel,
        string productId,
        string transactionId,
        string purchaseToken,
        CancellationToken ct = default);
}
