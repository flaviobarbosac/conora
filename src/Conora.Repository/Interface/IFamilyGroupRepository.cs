using Conora.Domain.Entities;

namespace Conora.Repository.Interface;

public interface IFamilyGroupRepository
{
    Task<FamilyGroupMember?> FindActiveMembershipAsync(Guid usuarioId, CancellationToken ct = default);
    Task<List<FamilyGroupMember>> ListActiveMembersAsync(Guid familyGroupId, CancellationToken ct = default);
    Task<int> CountActiveMembersAsync(Guid familyGroupId, CancellationToken ct = default);
    Task<FamilyGroup?> GetGroupAsync(Guid id, CancellationToken ct = default, bool track = true);
    void AddGroup(FamilyGroup group);
    void AddMember(FamilyGroupMember member);
    void SoftDelete(FamilyGroupMember member);
    void SoftDelete(FamilyGroup group);

    Task<FamilyInvite?> FindInviteByTokenAsync(string token, CancellationToken ct = default, bool track = false);
    Task<FamilyInvite?> GetInviteForInviterAsync(Guid inviteId, Guid inviterUsuarioId, CancellationToken ct = default);
    Task<List<FamilyInvite>> ListInvitesByInviterAsync(Guid inviterUsuarioId, CancellationToken ct = default);
    Task<bool> HasOpenInviteAsync(Guid inviterUsuarioId, string email, CancellationToken ct = default);
    Task<List<FamilyInvite>> ListOpenInvitesByGroupAsync(Guid familyGroupId, CancellationToken ct = default);
    void AddInvite(FamilyInvite invite);

    void AddOwnerTransfer(FamilyOwnerTransfer transfer);
    Task<FamilyOwnerTransfer?> FindOwnerTransferByTokenAsync(string token, CancellationToken ct = default, bool track = false);
    Task<FamilyOwnerTransfer?> FindOpenOwnerTransferAsync(Guid familyGroupId, CancellationToken ct = default, bool track = false);
    Task<FamilyOwnerTransfer?> FindAuthorizedHandoverAsync(Guid ownerUsuarioId, CancellationToken ct = default, bool track = false);
    Task<List<FamilyOwnerTransfer>> ListOpenOwnerTransfersForUserAsync(Guid usuarioId, CancellationToken ct = default);

    Task<List<FamilyNotice>> ListNoticesAsync(Guid usuarioId, int take, CancellationToken ct = default);
    Task<FamilyNotice?> GetNoticeAsync(Guid noticeId, Guid usuarioId, CancellationToken ct = default);
    void AddNotice(FamilyNotice notice);

    Task<User?> FindUserByIdAsync(Guid id, CancellationToken ct = default, bool track = false);
    Task<User?> FindUserByEmailAsync(string email, CancellationToken ct = default);
    Task<List<User>> ListUsersByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    /// <summary>Another workspace's subscription (cross-tenant), used when a family plan covers its members.</summary>
    Task<WorkspaceSubscription?> FindSubscriptionAsync(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Subscription linked to a store subscription id (cross-tenant), used by store notifications.</summary>
    Task<WorkspaceSubscription?> FindSubscriptionByStoreIdAsync(string store, string storeSubscriptionId, CancellationToken ct = default);

    /// <summary>
    /// Privileged write (bypasses tenant stamp): ends that login's plan and unlinks the store
    /// subscription so another login can take it over. Used for sandbox purchases only.
    /// </summary>
    Task<int> ReleaseStoreSubscriptionAsync(
        Guid usuarioId, string store, string storeSubscriptionId, DateTime endedAt, CancellationToken ct = default);
}
