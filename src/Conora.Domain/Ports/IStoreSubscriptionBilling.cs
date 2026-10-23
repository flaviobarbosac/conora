using Conora.Domain.Enums;

namespace Conora.Domain.Ports;

public enum StoreBillingChannel
{
    Google = 1,
    Apple = 2
}

/// <summary>Purchase reported by the app right after the native store sheet closes.</summary>
public sealed record StorePurchaseCommand(
    StoreBillingChannel Channel,
    string ProductId,
    string TransactionId,
    string PurchaseToken,
    Guid UsuarioId);

/// <summary>
/// Authenticated store server notification, already resolved to the subscription's current state.
/// <see cref="Evento"/> is informational (logs); the receipt state decides the entitlement.
/// </summary>
public sealed record StoreNoticeCommand(
    StoreBillingChannel Channel,
    string Evento,
    StoreReceipt Receipt);

/// <summary>Raised when a store notification is malformed or fails authentication.</summary>
public sealed class StoreNotificationRejectedException : Exception
{
    public StoreNotificationRejectedException(string message) : base(message) { }
}

/// <summary>
/// Store subscription billing for the user account (Play / App Store):
/// confirm a purchase from the app and keep the plan in sync with store notifications.
/// </summary>
public interface IStoreSubscriptionBilling
{
    Task ConfirmAsync(StorePurchaseCommand command, CancellationToken ct = default);

    Task ApplyNoticeAsync(StoreNoticeCommand command, CancellationToken ct = default);

    bool TryMapProduct(string productId, out PlanKind plan);
}
