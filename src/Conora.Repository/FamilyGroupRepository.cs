using Conora.Domain.Entities;
using Conora.Infrastructure.Persistence;
using Conora.Repository.Interface;
using Microsoft.EntityFrameworkCore;

namespace Conora.Repository;

public sealed class FamilyGroupRepository : IFamilyGroupRepository
{
    private readonly AppDbContext _db;

    public FamilyGroupRepository(AppDbContext db) => _db = db;

    public Task<FamilyGroupMember?> FindActiveMembershipAsync(Guid usuarioId, CancellationToken ct = default)
        => _db.FamilyGroupMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UsuarioId == usuarioId && m.DeletedAt == null, ct);

    public Task<List<FamilyGroupMember>> ListActiveMembersAsync(Guid familyGroupId, CancellationToken ct = default)
        => _db.FamilyGroupMembers.Where(m => m.FamilyGroupId == familyGroupId && m.DeletedAt == null).ToListAsync(ct);

    public Task<int> CountActiveMembersAsync(Guid familyGroupId, CancellationToken ct = default)
        => _db.FamilyGroupMembers.CountAsync(m => m.FamilyGroupId == familyGroupId && m.DeletedAt == null, ct);

    public Task<FamilyGroup?> GetGroupAsync(Guid id, CancellationToken ct = default, bool track = true)
    {
        IQueryable<FamilyGroup> query = _db.FamilyGroups;
        if (!track)
            query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(g => g.Id == id && g.DeletedAt == null, ct);
    }

    public void AddGroup(FamilyGroup group) => _db.FamilyGroups.Add(group);
    public void AddMember(FamilyGroupMember member) => _db.FamilyGroupMembers.Add(member);

    public void SoftDelete(FamilyGroupMember member)
    {
        if (member.DeletedAt is not null) return;
        member.DeletedAt = DateTime.UtcNow;
        member.Version += 1;
    }

    public void SoftDelete(FamilyGroup group)
    {
        if (group.DeletedAt is not null) return;
        group.DeletedAt = DateTime.UtcNow;
        group.Version += 1;
    }

    public Task<FamilyInvite?> FindInviteByTokenAsync(string token, CancellationToken ct = default, bool track = false)
    {
        IQueryable<FamilyInvite> query = _db.FamilyInvites;
        if (!track)
            query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(i => i.Token == token && i.DeletedAt == null, ct);
    }

    public Task<FamilyInvite?> GetInviteForInviterAsync(Guid inviteId, Guid inviterUsuarioId, CancellationToken ct = default)
        => _db.FamilyInvites.FirstOrDefaultAsync(
            i => i.Id == inviteId && i.InviterUsuarioId == inviterUsuarioId && i.DeletedAt == null, ct);

    public Task<List<FamilyInvite>> ListInvitesByInviterAsync(Guid inviterUsuarioId, CancellationToken ct = default)
        => _db.FamilyInvites.AsNoTracking()
            .Where(i => i.InviterUsuarioId == inviterUsuarioId && i.DeletedAt == null
                        && i.AcceptedAt == null && i.CancelledAt == null)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

    public Task<bool> HasOpenInviteAsync(Guid inviterUsuarioId, string email, CancellationToken ct = default)
        => _db.FamilyInvites.AnyAsync(
            i => i.InviterUsuarioId == inviterUsuarioId && i.Email == email && i.DeletedAt == null
                 && i.AcceptedAt == null && i.CancelledAt == null && i.ExpiresAt > DateTime.UtcNow, ct);

    public Task<List<FamilyInvite>> ListOpenInvitesByGroupAsync(Guid familyGroupId, CancellationToken ct = default)
        => _db.FamilyInvites
            .Where(i => i.FamilyGroupId == familyGroupId && i.AcceptedAt == null && i.CancelledAt == null && i.DeletedAt == null)
            .ToListAsync(ct);

    public void AddInvite(FamilyInvite invite) => _db.FamilyInvites.Add(invite);

    public Task<List<FamilyNotice>> ListNoticesAsync(Guid usuarioId, int take, CancellationToken ct = default)
        => _db.FamilyNotices.AsNoTracking()
            .Where(n => n.UsuarioId == usuarioId && n.DeletedAt == null)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public Task<FamilyNotice?> GetNoticeAsync(Guid noticeId, Guid usuarioId, CancellationToken ct = default)
        => _db.FamilyNotices.FirstOrDefaultAsync(n => n.Id == noticeId && n.UsuarioId == usuarioId && n.DeletedAt == null, ct);

    public void AddNotice(FamilyNotice notice) => _db.FamilyNotices.Add(notice);

    public Task<User?> FindUserByIdAsync(Guid id, CancellationToken ct = default, bool track = false)
    {
        IQueryable<User> query = _db.Users.IgnoreQueryFilters();
        if (!track)
            query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null, ct);
    }

    public Task<User?> FindUserByEmailAsync(string email, CancellationToken ct = default)
        => _db.Users.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email && u.DeletedAt == null, ct);

    public Task<List<User>> ListUsersByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        return _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => idList.Contains(u.Id) && u.DeletedAt == null)
            .ToListAsync(ct);
    }
}
