namespace Conora.Domain.Ports;

/// <summary>
/// Play / App Store subscription bridge. Full RTDN / App Store Server Notifications
/// wiring is stubbed; local WorkspaceSubscription follows cancel / confirm calls.
/// </summary>
public interface IStoreBillingGateway
{
    /// <summary>
    /// Cancels the member's own Individual store subscription when they accept a family invite,
    /// so they are not billed twice. No-op when there is no local Individual subscription.
    /// </summary>
    Task CancelMemberIndividualAsync(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Cancels the budget owner's covering store subscription when the owner leaves the group.
    /// Group members fall to free until a new owner pays on their own device.
    /// </summary>
    Task CancelOwnerSubscriptionAsync(Guid ownerUsuarioId, CancellationToken ct = default);
}
