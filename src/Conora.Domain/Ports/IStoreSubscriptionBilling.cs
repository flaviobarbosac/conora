using Conora.Domain.Enums;

namespace Conora.Domain.Ports;

public enum StoreBillingChannel
{
    Google = 1,
    Apple = 2
}

public sealed record StorePurchaseCommand(
    StoreBillingChannel Channel,
    string ProductId,
    string TransactionId,
    string PurchaseToken,
    Guid UsuarioId);

public sealed record StoreNoticeCommand(
    StoreBillingChannel Channel,
    string Evento,
    string ProductId,
    string TransactionId,
    string? SubscriptionId,
    Guid? UsuarioId,
    bool EmTrial = false);

/// <summary>
/// Store subscription billing for the user account (Play / App Store) —
/// Clampfy StoreAssinaturaService shape: confirm purchase + apply store notices.
/// </summary>
public interface IStoreSubscriptionBilling
{
    Task ConfirmAsync(StorePurchaseCommand command, CancellationToken ct = default);

    Task ApplyNoticeAsync(StoreNoticeCommand command, CancellationToken ct = default);

    bool TryMapProduct(string productId, out PlanKind plan);
}
