using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Services;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Conora.Services;

public sealed partial class FamilyGroupService
{
    public async Task<FamilyInviteResponse> InviteAsync(FamilyInviteRequest request, CancellationToken ct)
    {
        var self = RequireUserId();
        var email = request.Email.Trim().ToLowerInvariant();
        var me = await RequireUserAsync(ct);
        if (string.Equals(me.Email, email, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("email", "Você não pode convidar a si mesmo.");

        var membership = await _family.FindActiveMembershipAsync(self, ct);
        var now = DateTime.UtcNow;
        int extraPeople;
        if (membership is not null)
        {
            var group = await _family.GetGroupAsync(membership.FamilyGroupId, ct)
                        ?? throw new ValidationException("group", "Grupo não encontrado.");
            if (!group.IsBudgetOwner(self))
                throw new ForbiddenException("Só o responsável pelo grupo pode convidar.");

            var count = await _family.CountActiveMembersAsync(membership.FamilyGroupId, ct);
            if (count >= FamilyGroup.MaxMembers)
                throw new ValidationException("email", $"O grupo já tem {FamilyGroup.MaxMembers} membros.");

            var openInvites = await _family.ListOpenInvitesByGroupAsync(membership.FamilyGroupId, ct);
            extraPeople = count - 1 + openInvites.Count(i => i.ExpiresAt > now);
        }
        else
        {
            var openInvites = await _family.ListInvitesByInviterAsync(self, ct);
            extraPeople = openInvites.Count(i => i.ExpiresAt > now);
        }

        await _plan.EnsureCanAddFamilyMemberAsync(extraPeople, ct);

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

        // Invite opens the store/plans first; the token is kept so accept can cancel Individual.
        var link = $"{PublicWebBase()}/app/planos?convite={Uri.EscapeDataString(invite.Token)}";
        try
        {
            await _email.SendAsync(
                email,
                $"{me.Name} convidou você para o grupo no Conora",
                new EmailContent(
                    $"{me.Name} convidou você",
                    [
                        $"{me.Name} convidou você para compartilhar o orçamento familiar no Conora.",
                        "Abra o link na loja do app (Google Play ou App Store), assine o plano do grupo se ainda não tiver, e depois aceite o convite com este mesmo e-mail.",
                        "Se você já paga o Individual sozinho, o aceite cancela essa cobrança para não pagar em dobro."
                    ])
                {
                    Preheader = "Convite para o grupo familiar no Conora — abre a loja.",
                    ActionLabel = "Abrir loja e aceitar",
                    ActionUrl = link,
                    Note = $"O convite vale até {invite.ExpiresAt:dd/MM/yyyy HH:mm} UTC."
                },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Family invite e-mail failed for {Email}; invite kept.", email);
        }

        AuditRecorder.Record(_audits, _correlation, "FamilyInvite", invite.Id, "FamilyInviteSent", new { email });
        await _uow.SaveChangesAsync(ct);
        return ToInvite(invite);
    }

    public async Task<FamilyQrInviteResponse> CreateQrInviteAsync(CancellationToken ct)
    {
        var self = RequireUserId();
        var membership = await _family.FindActiveMembershipAsync(self, ct);
        var now = DateTime.UtcNow;
        int extraPeople;
        if (membership is not null)
        {
            var group = await _family.GetGroupAsync(membership.FamilyGroupId, ct)
                        ?? throw new ValidationException("group", "Grupo não encontrado.");
            if (!group.IsBudgetOwner(self))
                throw new ForbiddenException("Só o responsável pelo orçamento gera o QR do grupo.");

            var count = await _family.CountActiveMembersAsync(group.Id, ct);
            if (count >= FamilyGroup.MaxMembers)
                throw new ValidationException("group", $"O grupo já tem {FamilyGroup.MaxMembers} membros.");

            var openInvites = await _family.ListOpenInvitesByGroupAsync(membership.FamilyGroupId, ct);
            extraPeople = count - 1 + openInvites.Count(i => i.ExpiresAt > now && !i.IsQrInvite);
        }
        else
        {
            var openInvites = await _family.ListInvitesByInviterAsync(self, ct);
            extraPeople = openInvites.Count(i => i.ExpiresAt > now && i.IsOpen && !i.IsQrInvite);
        }

        await _plan.EnsureCanAddFamilyMemberAsync(extraPeople, ct);

        var openQr = (await _family.ListInvitesByInviterAsync(self, ct))
            .Where(i => i.IsOpen && i.IsQrInvite)
            .ToList();
        foreach (var previous in openQr)
            previous.Cancel();

        var invite = FamilyInvite.CreateQr(self, membership?.FamilyGroupId, QrInviteTtl);
        _family.AddInvite(invite);
        AuditRecorder.Record(_audits, _correlation, "FamilyInvite", invite.Id, "FamilyQrInviteCreated", null);
        await _uow.SaveChangesAsync(ct);

        var joinUrl = $"{PublicWebBase()}/app/grupo/convite/{invite.Token}";
        return new FamilyQrInviteResponse(invite.Id, invite.Token, invite.ExpiresAt, joinUrl);
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
        if (!invite.IsQrInvite
            && !string.Equals(user.Email, invite.Email, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("token", "Entre com o e-mail convidado para aceitar.");

        var existing = await _family.FindActiveMembershipAsync(self, ct);
        if (existing is not null)
            throw new ValidationException("token", "Você já faz parte de um grupo.");

        FamilyGroup group;
        var firstFormation = false;
        if (invite.FamilyGroupId is Guid gid)
        {
            group = await _family.GetGroupAsync(gid, ct)
                    ?? throw new ValidationException("token", "Grupo do convite não encontrado.");
            if (!group.IsBudgetOwner(invite.InviterUsuarioId))
                throw new ValidationException("token", "Quem convidou não é mais o responsável pelo grupo.");
            if (await _family.CountActiveMembersAsync(gid, ct) >= FamilyGroup.MaxMembers)
                throw new ValidationException("token", $"O grupo já tem {FamilyGroup.MaxMembers} membros.");
            await EnsureOwnerSeatAsync(group.BudgetOwnerUsuarioId, await _family.CountActiveMembersAsync(gid, ct) - 1, ct);
        }
        else
        {
            var inviterMembership = await _family.FindActiveMembershipAsync(invite.InviterUsuarioId, ct);
            if (inviterMembership is not null)
            {
                group = await _family.GetGroupAsync(inviterMembership.FamilyGroupId, ct)
                        ?? throw new ValidationException("token", "Grupo do convidante não encontrado.");
                if (!group.IsBudgetOwner(invite.InviterUsuarioId))
                    throw new ValidationException("token", "Quem convidou não é mais o responsável pelo grupo.");
                if (await _family.CountActiveMembersAsync(group.Id, ct) >= FamilyGroup.MaxMembers)
                    throw new ValidationException("token", $"O grupo já tem {FamilyGroup.MaxMembers} membros.");
                await EnsureOwnerSeatAsync(
                    group.BudgetOwnerUsuarioId,
                    await _family.CountActiveMembersAsync(group.Id, ct) - 1,
                    ct);
            }
            else
            {
                await EnsureOwnerSeatAsync(invite.InviterUsuarioId, 0, ct);
                group = FamilyGroup.Create(invite.InviterUsuarioId);
                _family.AddGroup(group);
                _family.AddMember(FamilyGroupMember.Create(group.Id, invite.InviterUsuarioId));
                firstFormation = true;
            }

            invite.BindGroup(group.Id);
        }

        _family.AddMember(FamilyGroupMember.Create(group.Id, self));
        invite.MarkAccepted(self);

        await MarkUserDataAsFamilyAsync(self, ct);
        if (firstFormation)
            await MarkUserDataAsFamilyAsync(invite.InviterUsuarioId, ct);

        _family.AddNotice(FamilyNotice.Create(
            invite.InviterUsuarioId,
            "InviteAccepted",
            $"{user.Name} aceitou o convite e entrou no seu grupo."));

        var inviter = await _family.FindUserByIdAsync(invite.InviterUsuarioId, ct);
        if (inviter is not null)
        {
            try
            {
                await _email.SendAsync(
                    inviter.Email,
                    $"{user.Name} entrou no seu grupo no Conora",
                    new EmailContent(
                        $"{user.Name} entrou no grupo",
                        [$"{user.Name} ({user.Email}) aceitou seu convite e agora faz parte do grupo familiar no Conora."])
                    {
                        Preheader = "Novo membro no grupo familiar."
                    },
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invite-accepted e-mail failed for {Email}.", inviter.Email);
            }
        }

        AuditRecorder.Record(_audits, _correlation, "FamilyInvite", invite.Id, "FamilyInviteAccepted", new { invite.Email, self });
        await _uow.SaveChangesAsync(ct);
        // Avoid double billing: cancel the invitee's own Individual (local stub; store API TODO).
        await _storeBilling.CancelMemberIndividualAsync(self, ct);
        await _categories.EnsureDefaultsAsync(ct);
        await _categories.MergeOnJoinAsync(group.BudgetOwnerUsuarioId, self, ct);
        return await GetGroupAsync(ct);
    }

    private async Task EnsureOwnerSeatAsync(Guid ownerId, int currentExtraMembers, CancellationToken ct)
    {
        var sub = await _family.FindSubscriptionAsync(ownerId, ct);
        var tier = sub is not null && sub.IsWritable(DateTime.UtcNow) ? sub.Tier : PlanTier.Individual;
        var max = PlanCatalog.MaxExtraMembers(tier) ?? 0;
        if (currentExtraMembers >= max)
            throw new ValidationException("token", PlanCatalog.FamilySeatMessage(tier));
    }

    public async Task LeaveAsync(CancellationToken ct)
    {
        var self = RequireUserId();
        var membership = await _family.FindActiveMembershipAsync(self, ct)
                         ?? throw new ValidationException("group", "Você não está em um grupo.");
        var group = await _family.GetGroupAsync(membership.FamilyGroupId, ct)
                    ?? throw new ValidationException("group", "Grupo não encontrado.");

        var count = await _family.CountActiveMembersAsync(membership.FamilyGroupId, ct);
        if (group.IsBudgetOwner(self) && count > 1)
            throw new ValidationException(
                "group",
                "Responsável pelo orçamento não pode sair sem transferir o orçamento para outro membro.");

        var wasOwner = group.IsBudgetOwner(self);
        await RemoveMemberCoreAsync(membership.FamilyGroupId, self, self, markPersonal: true, ct);
        if (wasOwner)
            await _storeBilling.CancelOwnerSubscriptionAsync(self, ct);
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
        var group = await _family.GetGroupAsync(membership.FamilyGroupId, ct)
                    ?? throw new ValidationException("group", "Grupo não encontrado.");
        if (!group.IsBudgetOwner(self))
            throw new ForbiddenException("Só o responsável pelo orçamento pode remover membros.");

        var members = await _family.ListActiveMembersAsync(membership.FamilyGroupId, ct);
        if (members.All(m => m.UsuarioId != usuarioId))
            throw new NotFoundException("Membro do grupo", usuarioId);
        if (group.IsBudgetOwner(usuarioId))
            throw new ValidationException("usuarioId", "Não é possível remover o responsável pelo orçamento.");

        await RemoveMemberCoreAsync(membership.FamilyGroupId, usuarioId, self, markPersonal: true, ct);
    }

    private async Task RemoveMemberCoreAsync(
        Guid groupId,
        Guid removedUsuarioId,
        Guid actorUsuarioId,
        bool markPersonal,
        CancellationToken ct)
    {
        var members = await _family.ListActiveMembersAsync(groupId, ct);
        var row = members.FirstOrDefault(m => m.UsuarioId == removedUsuarioId)
                  ?? throw new NotFoundException("Membro do grupo", removedUsuarioId);
        _family.SoftDelete(row);

        var heir = members.FirstOrDefault(m => m.UsuarioId != removedUsuarioId)?.UsuarioId;
        var groupOwner = await _family.GetGroupAsync(groupId, ct, track: false);
        if (groupOwner is not null
            && groupOwner.BudgetOwnerUsuarioId != removedUsuarioId
            && members.Any(m => m.UsuarioId == groupOwner.BudgetOwnerUsuarioId))
            heir = groupOwner.BudgetOwnerUsuarioId;
        if (heir is Guid heirId)
            await _repo.ReassignCategoryCreatorAsync(removedUsuarioId, heirId, ct);

        if (markPersonal)
            await MarkUserDataAsPersonalAsync(removedUsuarioId, ct);

        var remaining = await _family.CountActiveMembersAsync(groupId, ct);
        if (remaining <= 1)
        {
            var openInvites = await _family.ListOpenInvitesByGroupAsync(groupId, ct);
            foreach (var invite in openInvites)
                invite.Cancel();

            var pending = await _family.FindOpenOwnerTransferAsync(groupId, ct, track: true);
            pending?.Cancel();

            if (remaining == 1)
            {
                var last = (await _family.ListActiveMembersAsync(groupId, ct)).First();
                _family.SoftDelete(last);
                await MarkUserDataAsPersonalAsync(last.UsuarioId, ct);
            }

            var group = await _family.GetGroupAsync(groupId, ct);
            if (group is not null)
                _family.SoftDelete(group);
        }

        AuditRecorder.Record(_audits, _correlation, "FamilyGroup", groupId,
            removedUsuarioId == actorUsuarioId ? "FamilyLeft" : "FamilyMemberRemoved",
            new { removedUsuarioId, actorUsuarioId });
        await _uow.SaveChangesAsync(ct);
    }

    private async Task MarkUserDataAsFamilyAsync(Guid usuarioId, CancellationToken ct)
    {
        // ExecuteUpdate — bypasses StampTenant so acceptor can flip the inviter's data on first join.
        await _repo.SetCategorySharingForUsuarioAsync(usuarioId, SharingScope.Family, ct);
        await _repo.SetLifeProjectScopeForUsuarioAsync(usuarioId, LifeProjectScope.Group, ct);
    }

    private async Task MarkUserDataAsPersonalAsync(Guid usuarioId, CancellationToken ct)
    {
        // Categories stay Family by default (opt-out model). Only life projects reset to Personal.
        await _repo.SetLifeProjectScopeForUsuarioAsync(usuarioId, LifeProjectScope.Personal, ct);
    }

    public async Task MarkNoticeReadAsync(Guid noticeId, CancellationToken ct)
    {
        var self = RequireUserId();
        var notice = await _family.GetNoticeAsync(noticeId, self, ct)
                     ?? throw new NotFoundException("Aviso", noticeId);
        notice.MarkRead();
        await _uow.SaveChangesAsync(ct);
    }
}
