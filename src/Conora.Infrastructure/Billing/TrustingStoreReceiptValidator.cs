using Conora.Domain.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conora.Infrastructure.Billing;

/// <summary>
/// Stub until Google/Apple credentials are live: trusts productId + token from the client/webhook.
/// When service-account / App Store Server API paths are set, replace with a real validator (Clampfy pattern).
/// </summary>
public sealed class TrustingStoreReceiptValidator : IStoreReceiptValidator
{
    private readonly StoreBillingOptions _options;
    private readonly ILogger<TrustingStoreReceiptValidator> _logger;

    public TrustingStoreReceiptValidator(
        IOptions<StoreBillingOptions> options,
        ILogger<TrustingStoreReceiptValidator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<StoreReceipt> ValidateAsync(
        StoreBillingChannel channel,
        string productId,
        string transactionId,
        string purchaseToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(transactionId))
            throw new InvalidOperationException("Recibo da loja incompleto.");
        if (string.IsNullOrWhiteSpace(purchaseToken))
            throw new InvalidOperationException("Token da loja obrigatório.");

        if (HasLiveCredentials())
        {
            _logger.LogWarning(
                "Store credentials are configured but Google/Apple API validation is not wired yet; trusting payload for {ProductId}.",
                productId);
        }

        return Task.FromResult(new StoreReceipt(
            productId.Trim(),
            transactionId.Trim(),
            SubscriptionId: null,
            Paid: true,
            InTrial: false,
            InGrace: false,
            Amount: null,
            AccountId: null));
    }

    private bool HasLiveCredentials() =>
        !string.IsNullOrWhiteSpace(_options.GoogleServiceAccountJsonPath)
        || !string.IsNullOrWhiteSpace(_options.ApplePrivateKeyPath);
}
