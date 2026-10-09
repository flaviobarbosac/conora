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

    Task<List<FamilyNotice>> ListNoticesAsync(Guid usuarioId, int take, CancellationToken ct = default);
    Task<FamilyNotice?> GetNoticeAsync(Guid noticeId, Guid usuarioId, CancellationToken ct = default);
    void AddNotice(FamilyNotice notice);

    Task<User?> FindUserByIdAsync(Guid id, CancellationToken ct = default, bool track = false);
    Task<User?> FindUserByEmailAsync(string email, CancellationToken ct = default);
    Task<List<User>> ListUsersByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
}
