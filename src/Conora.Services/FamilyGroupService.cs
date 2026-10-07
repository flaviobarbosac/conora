using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;
using Microsoft.Extensions.Configuration;

namespace Conora.Services;

public sealed class FamilyGroupService
{
    private static readonly TimeSpan InviteTtl = TimeSpan.FromDays(7);

    private readonly IFamilyGroupRepository _family;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenant;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly IAuditEventRepository _audits;
    private readonly ICorrelationContext _correlation;

    public FamilyGroupService(
        IFamilyGroupRepository family,
        IUnitOfWork uow,
        ITenantContext tenant,
        IEmailSender email,
        IConfiguration config,
        IAuditEventRepository audits,
        ICorrelationContext correlation)
    {
        _family = family;
        _uow = uow;
        _tenant = tenant;
        _email = email;
        _config = config;
        _audits = audits;
        _correlation = correlation;
    }

    public Guid RequireUserId() => _tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");

    /// <summary>Current user plus peers when in a family group; otherwise only the current user.</summary>
    public async Task<IReadOnlyList<Guid>> GetReadableUsuarioIdsAsync(CancellationToken ct)
    {
        var self = RequireUserId();
        var membership = await _family.FindActiveMembershipAsync(self, ct);
        if (membership is null)
            return [self];

        var members = await _family.ListActiveMembersAsync(membership.FamilyGroupId, ct);
        var ids = members.Select(m => m.UsuarioId).Distinct().ToList();
        return ids.Count == 0 ? [self] : ids;
    }

    public async Task<bool> IsInSameGroupAsync(Guid otherUsuarioId, CancellationToken ct)
    {
        var self = RequireUserId();
        if (self == otherUsuarioId)
            return true;
        var membership = await _family.FindActiveMembershipAsync(self, ct);
        if (membership is null)
            return false;
        var members = await _family.ListActiveMembersAsync(membership.FamilyGroupId, ct);
        return members.Any(m => m.UsuarioId == otherUsuarioId);
    }

    public async Task<ProfileResponse> GetProfileAsync(CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        return new ProfileResponse(user.Id, user.Name, user.Email);
    }

    public async Task<ProfileResponse> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await RequireUserAsync(ct, track: true);
        user.Rename(request.Name);
        await _uow.SaveChangesAsync(ct);
        return new ProfileResponse(user.Id, user.Name, user.Email);
    }

    public async Task<FamilyGroupResponse> GetGroupAsync(CancellationToken ct)
    {
        var self = RequireUserId();
        var membership = await _family.FindActiveMembershipAsync(self, ct);
        var invites = await _family.ListInvitesByInviterAsync(self, ct);
        var notices = await _family.ListNoticesAsync(self, 20, ct);

        if (membership is null)
        {
            var me = await RequireUserAsync(ct);
            return new FamilyGroupResponse(
                null,
                [new FamilyMemberUserResponse(me.Id, me.Name, me.Email, true)],
                invites.Select(ToInvite).ToList(),
                notices.Select(ToNotice).ToList());
        }

        var memberRows = await _family.ListActiveMembersAsync(membership.FamilyGroupId, ct);
        var users = await _family.ListUsersByIdsAsync(memberRows.Select(m => m.UsuarioId), ct);

        return new FamilyGroupResponse(
            membership.FamilyGroupId,
            users.OrderBy(u => u.Name).Select(u => new FamilyMemberUserResponse(u.Id, u.Name, u.Email, u.Id == self)).ToList(),
            invites.Select(ToInvite).ToList(),
            notices.Select(ToNotice).ToList());
    }

    public async Task<FamilyInviteResponse> InviteAsync(FamilyInviteRequest request, CancellationToken ct)
    {
        var self = RequireUserId();
        var email = request.Email.Trim().ToLowerInvariant();
        var me = await RequireUserAsync(ct);
        if (string.Equals(me.Email, email, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("email", "Você não pode convidar a si mesmo.");

        var membership = await _family.FindActiveMembershipAsync(self, ct);
        if (membership is not null)
        {
            var count = await _family.CountActiveMembersAsync(membership.FamilyGroupId, ct);
            if (count >= 2)
                throw new ValidationException("email", "O grupo já tem dois membros.");
        }

        var invitee = await _family.FindUserByEmailAsync(email, ct);
        if (invitee is not null)
        {
            var otherMembership = await _family.FindActiveMembershipAsync(invitee.Id, ct);
            if (otherMembership is not null)
                throw new ValidationException("email", "Esta pessoa já faz parte de um grupo.");
        }

        if (await _family.HasOpenInviteAsync(self, email, ct))
            throw new ValidationException("email", "Já existe um convite aberto para este e-mail.");

        var invite = FamilyInvite.Create(self, email, membership?.FamilyGroupId, InviteTtl);
        _family.AddInvite(invite);

        var link = $"{PublicWebBase()}/app/grupo/convite/{invite.Token}";
        await _email.SendAsync(
            email,
            $"{me.Name} convidou você para o grupo no Conora",
            $"Olá,\n\n{me.Name} convidou você para compartilhar o orçamento familiar no Conora.\n\nAbra o link para entrar (é preciso usar este e-mail): {link}\n\nO convite vale até {invite.ExpiresAt:dd/MM/yyyy HH:mm} UTC.\n",
            ct);

        AuditRecorder.Record(_audits, _correlation, "FamilyInvite", invite.Id, "FamilyInviteSent", new { email });
        await _uow.SaveChangesAsync(ct);
        return ToInvite(invite);
    }

    public async Task CancelInviteAsync(Guid inviteId, CancellationToken ct)
    {
        var self = RequireUserId();
        var invite = await _family.GetInviteForInviterAsync(inviteId, self, ct)
                     ?? throw new NotFoundException("Convite", inviteId);
        invite.Cancel();
        AuditRecorder.Record(_audits, _correlation, "FamilyInvite", invite.Id, "FamilyInviteCancelled", new { invite.Email });
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<FamilyInvitePreviewResponse> PreviewInviteAsync(string token, CancellationToken ct)
    {
        var invite = await _family.FindInviteByTokenAsync(token, ct);
        if (invite is null)
            return new FamilyInvitePreviewResponse("Invalid", "", "", null);

        var inviter = await _family.FindUserByIdAsync(invite.InviterUsuarioId, ct);

        if (invite.CancelledAt is not null)
            return new FamilyInvitePreviewResponse("Cancelled", inviter?.Name ?? "", invite.Email, invite.ExpiresAt);
        if (invite.AcceptedAt is not null)
            return new FamilyInvitePreviewResponse("Accepted", inviter?.Name ?? "", invite.Email, invite.ExpiresAt);
        if (invite.ExpiresAt <= DateTime.UtcNow)
            return new FamilyInvitePreviewResponse("Expired", inviter?.Name ?? "", invite.Email, invite.ExpiresAt);

        return new FamilyInvitePreviewResponse("Open", inviter?.Name ?? "", invite.Email, invite.ExpiresAt);
    }

    public async Task<FamilyGroupResponse> AcceptInviteAsync(string token, CancellationToken ct)
    {
        var self = RequireUserId();
        var user = await RequireUserAsync(ct);
        var invite = await _family.FindInviteByTokenAsync(token, ct, track: true)
                     ?? throw new ValidationException("token", "Convite inválido.");

        if (invite.AcceptedAt is not null && invite.AcceptedByUsuarioId == self)
            return await GetGroupAsync(ct);

        if (invite.CancelledAt is not null)
            throw new ValidationException("token", "Este convite foi cancelado.");
        if (invite.ExpiresAt <= DateTime.UtcNow)
            throw new ValidationException("token", "Este convite expirou.");
        if (invite.AcceptedAt is not null)
            throw new ValidationException("token", "Este convite já foi usado.");
        if (!string.Equals(user.Email, invite.Email, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("token", "Entre com o e-mail convidado para aceitar.");

        var existing = await _family.FindActiveMembershipAsync(self, ct);
        if (existing is not null)
            throw new ValidationException("token", "Você já faz parte de um grupo.");

        FamilyGroup group;
        if (invite.FamilyGroupId is Guid gid)
        {
            group = await _family.GetGroupAsync(gid, ct)
                    ?? throw new ValidationException("token", "Grupo do convite não encontrado.");
            if (await _family.CountActiveMembersAsync(gid, ct) >= 2)
                throw new ValidationException("token", "O grupo já está completo.");
        }
        else
        {
            var inviterMembership = await _family.FindActiveMembershipAsync(invite.InviterUsuarioId, ct);
            if (inviterMembership is not null)
            {
                group = await _family.GetGroupAsync(inviterMembership.FamilyGroupId, ct)
                        ?? throw new ValidationException("token", "Grupo do convidante não encontrado.");
                if (await _family.CountActiveMembersAsync(group.Id, ct) >= 2)
                    throw new ValidationException("token", "O grupo já está completo.");
            }
            else
            {
                group = FamilyGroup.Create(invite.InviterUsuarioId);
                _family.AddGroup(group);
                _family.AddMember(FamilyGroupMember.Create(group.Id, invite.InviterUsuarioId));
            }

            invite.BindGroup(group.Id);
        }

        _family.AddMember(FamilyGroupMember.Create(group.Id, self));
        invite.MarkAccepted(self);

        _family.AddNotice(FamilyNotice.Create(
            invite.InviterUsuarioId,
            "InviteAccepted",
            $"{user.Name} aceitou o convite e entrou no seu grupo."));

        var inviter = await _family.FindUserByIdAsync(invite.InviterUsuarioId, ct);
        if (inviter is not null)
        {
            await _email.SendAsync(
                inviter.Email,
                $"{user.Name} entrou no seu grupo no Conora",
                $"Olá,\n\n{user.Name} ({user.Email}) aceitou seu convite e agora faz parte do grupo familiar no Conora.\n",
                ct);
        }

        AuditRecorder.Record(_audits, _correlation, "FamilyInvite", invite.Id, "FamilyInviteAccepted", new { invite.Email, self });
        await _uow.SaveChangesAsync(ct);
        return await GetGroupAsync(ct);
    }

    public async Task LeaveAsync(CancellationToken ct)
    {
        var self = RequireUserId();
        await DissolveGroupForUserAsync(self, actorUsuarioId: self, ct);
    }

    public async Task RemoveMemberAsync(Guid usuarioId, CancellationToken ct)
    {
        var self = RequireUserId();
        if (usuarioId == self)
        {
            await LeaveAsync(ct);
            return;
        }

        var membership = await _family.FindActiveMembershipAsync(self, ct)
                         ?? throw new ValidationException("group", "Você não está em um grupo.");
        var members = await _family.ListActiveMembersAsync(membership.FamilyGroupId, ct);
        if (members.All(m => m.UsuarioId != usuarioId))
            throw new NotFoundException("Membro do grupo", usuarioId);

        await DissolveGroupAsync(membership.FamilyGroupId, removedUsuarioId: usuarioId, actorUsuarioId: self, ct);
    }

    public async Task MarkNoticeReadAsync(Guid noticeId, CancellationToken ct)
    {
        var self = RequireUserId();
        var notice = await _family.GetNoticeAsync(noticeId, self, ct)
                     ?? throw new NotFoundException("Aviso", noticeId);
        notice.MarkRead();
        await _uow.SaveChangesAsync(ct);
    }

    private async Task DissolveGroupForUserAsync(Guid usuarioId, Guid actorUsuarioId, CancellationToken ct)
    {
        var membership = await _family.FindActiveMembershipAsync(usuarioId, ct)
                         ?? throw new ValidationException("group", "Você não está em um grupo.");
        await DissolveGroupAsync(membership.FamilyGroupId, removedUsuarioId: usuarioId, actorUsuarioId: actorUsuarioId, ct);
    }

    private async Task DissolveGroupAsync(Guid groupId, Guid removedUsuarioId, Guid actorUsuarioId, CancellationToken ct)
    {
        var members = await _family.ListActiveMembersAsync(groupId, ct);
        foreach (var member in members)
            _family.SoftDelete(member);

        var group = await _family.GetGroupAsync(groupId, ct);
        if (group is not null)
            _family.SoftDelete(group);

        var openInvites = await _family.ListOpenInvitesByGroupAsync(groupId, ct);
        foreach (var invite in openInvites)
            invite.Cancel();

        AuditRecorder.Record(_audits, _correlation, "FamilyGroup", groupId,
            removedUsuarioId == actorUsuarioId ? "FamilyLeft" : "FamilyMemberRemoved",
            new { removedUsuarioId, actorUsuarioId });
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<User> RequireUserAsync(CancellationToken ct, bool track = false)
    {
        var id = RequireUserId();
        return await _family.FindUserByIdAsync(id, ct, track)
               ?? throw new UserNotFoundException(id);
    }

    private string PublicWebBase()
    {
        var configured = _config["App:PublicWebUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');
        var cors = _config["Cors:Origins:0"];
        if (!string.IsNullOrWhiteSpace(cors))
            return cors.TrimEnd('/');
        return "https://conora.com.br";
    }

    private static FamilyInviteResponse ToInvite(FamilyInvite i)
        => new(i.Id, i.Email, i.ExpiresAt, i.AcceptedAt, i.CancelledAt, i.IsOpen);

    private static FamilyNoticeResponse ToNotice(FamilyNotice n)
        => new(n.Id, n.Kind, n.Message, n.CreatedAt, n.ReadAt is not null);
}
