using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Conora.Services;

public sealed class FamilyGroupService
{
    private static readonly TimeSpan InviteTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan QrInviteTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan OwnerTransferTtl = TimeSpan.FromDays(7);

    private readonly IFamilyGroupRepository _family;
    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenant;
    private readonly IEmailSender _email;
    private readonly IEmailSuppressionStore _emailSuppressions;
    private readonly IConfiguration _config;
    private readonly IAuditEventRepository _audits;
    private readonly ICorrelationContext _correlation;
    private readonly ILogger<FamilyGroupService> _logger;
    private readonly CategoryService _categories;
    private readonly PlanService _plan;

    public FamilyGroupService(
        IFamilyGroupRepository family,
        IFinanceRepository repo,
        IUnitOfWork uow,
        ITenantContext tenant,
        IEmailSender email,
        IEmailSuppressionStore emailSuppressions,
        IConfiguration config,
        IAuditEventRepository audits,
        ICorrelationContext correlation,
        ILogger<FamilyGroupService> logger,
        CategoryService categories,
        PlanService plan)
    {
        _plan = plan;
        _family = family;
        _repo = repo;
        _uow = uow;
        _tenant = tenant;
        _email = email;
        _emailSuppressions = emailSuppressions;
        _config = config;
        _audits = audits;
        _correlation = correlation;
        _logger = logger;
        _categories = categories;
    }

    public Guid RequireUserId() => _tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");

    /// <summary>
    /// Users whose Family-scoped data is readable. Personal scope always returns only self.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetReadableUsuarioIdsAsync(
        CancellationToken ct,
        SharingScope? scope = null)
    {
        var self = RequireUserId();
        var effective = await ResolveSharingScopeAsync(scope, ct);
        if (effective == SharingScope.Personal)
            return [self];

        var membership = await _family.FindActiveMembershipAsync(self, ct);
        if (membership is null)
            return [self];

        var members = await _family.ListActiveMembersAsync(membership.FamilyGroupId, ct);
        var ids = members.Select(m => m.UsuarioId).Distinct().ToList();
        return ids.Count == 0 ? [self] : ids;
    }

    public Task<SharingScope> ResolveSharingScopeAsync(SharingScope? requested, CancellationToken ct)
    {
        // Alone or in group: default Family (chart is Family-first; Personal is opt-out).
        _ = ct;
        return Task.FromResult(requested ?? SharingScope.Family);
    }

    public async Task<(FamilyGroup Group, bool IsOwner)> RequireGroupAsync(CancellationToken ct)
    {
        var self = RequireUserId();
        var membership = await _family.FindActiveMembershipAsync(self, ct)
                         ?? throw new ValidationException("group", "Você não está em um grupo.");
        var group = await _family.GetGroupAsync(membership.FamilyGroupId, ct)
                    ?? throw new ValidationException("group", "Grupo não encontrado.");
        return (group, group.IsBudgetOwner(self));
    }

    public async Task EnsureCanEditGroupBudgetAsync(CancellationToken ct)
    {
        // Alone (Family-default chart): anyone edits their own budget.
        // In a group: only the budget owner may edit the consolidated planned.
        var self = RequireUserId();
        var membership = await _family.FindActiveMembershipAsync(self, ct);
        if (membership is null)
            return;

        var group = await _family.GetGroupAsync(membership.FamilyGroupId, ct)
                    ?? throw new ValidationException("group", "Grupo não encontrado.");
        if (!group.IsBudgetOwner(self))
            throw new ForbiddenException("Só o responsável pelo orçamento familiar pode editá-lo.");
    }

    public async Task<Guid?> GetBudgetOwnerUsuarioIdAsync(CancellationToken ct)
    {
        var self = RequireUserId();
        var membership = await _family.FindActiveMembershipAsync(self, ct);
        if (membership is null)
            return null;
        var group = await _family.GetGroupAsync(membership.FamilyGroupId, ct, track: false);
        return group?.BudgetOwnerUsuarioId;
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

    public async Task<bool> IsBudgetOwnerWithPeersAsync(CancellationToken ct)
    {
        var self = RequireUserId();
        var membership = await _family.FindActiveMembershipAsync(self, ct);
        if (membership is null)
            return false;
        var group = await _family.GetGroupAsync(membership.FamilyGroupId, ct, track: false);
        if (group is null || !group.IsBudgetOwner(self))
            return false;
        return await _family.CountActiveMembersAsync(membership.FamilyGroupId, ct) > 1;
    }

    public async Task<ProfileResponse> GetProfileAsync(CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        return ToProfile(user);
    }

    public async Task<ProfileResponse> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await RequireUserAsync(ct, track: true);
        user.Rename(request.Name);
        await _uow.SaveChangesAsync(ct);
        return ToProfile(user);
    }

    public async Task<ProfileResponse> UpdatePreferencesAsync(UpdatePreferencesRequest request, CancellationToken ct)
    {
        var user = await RequireUserAsync(ct, track: true);
        user.UpdatePreferences(
            ThemeCatalog.Normalize(request.ThemeId),
            request.QuietMode,
            request.EmailsEnabled,
            request.ReserveTargetMonths,
            request.MonthCloseDay,
            request.IncludeAbsoluteValuesInRetrospective,
            request.VoiceEntryConfirm);
        await _uow.SaveChangesAsync(ct);

        if (request.EmailsEnabled)
            await _emailSuppressions.RemoveUserOptOutAsync(user.Email, ct);
        else
            await _emailSuppressions.UpsertAsync(user.Email, EmailSuppression.ReasonUserOptOut, null, ct);

        return ToProfile(user);
    }

    public async Task<ProfileResponse> CompleteOnboardingAsync(CancellationToken ct)
    {
        var user = await RequireUserAsync(ct, track: true);
        user.CompleteOnboarding();
        await _uow.SaveChangesAsync(ct);
        return ToProfile(user);
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
                null,
                false,
                true,
                [new FamilyMemberUserResponse(me.Id, me.Name, me.Email, true, false)],
                invites.Select(ToInvite).ToList(),
                notices.Select(ToNotice).ToList(),
                null);
        }

        var group = await _family.GetGroupAsync(membership.FamilyGroupId, ct, track: false)
                    ?? throw new ValidationException("group", "Grupo não encontrado.");
        var memberRows = await _family.ListActiveMembersAsync(membership.FamilyGroupId, ct);
        var users = await _family.ListUsersByIdsAsync(memberRows.Select(m => m.UsuarioId), ct);
        var isOwner = group.IsBudgetOwner(self);
        var canLeave = !isOwner || memberRows.Count <= 1;
        var pending = await _family.FindOpenOwnerTransferAsync(membership.FamilyGroupId, ct);
        FamilyOwnerTransferResponse? pendingDto = null;
        if (pending is not null)
        {
            var from = users.FirstOrDefault(u => u.Id == pending.FromUsuarioId);
            var to = users.FirstOrDefault(u => u.Id == pending.ToUsuarioId)
                     ?? await _family.FindUserByIdAsync(pending.ToUsuarioId, ct);
            pendingDto = new FamilyOwnerTransferResponse(
                pending.Id,
                pending.FromUsuarioId,
                from?.Name ?? "",
                pending.ToUsuarioId,
                to?.Name ?? "",
                pending.ExpiresAt,
                pending.IsOpen,
                pending.ToUsuarioId == self);
        }

        return new FamilyGroupResponse(
            membership.FamilyGroupId,
            group.BudgetOwnerUsuarioId,
            isOwner,
            canLeave,
            users.OrderBy(u => u.Name)
                .Select(u => new FamilyMemberUserResponse(
                    u.Id, u.Name, u.Email, u.Id == self, group.IsBudgetOwner(u.Id)))
                .ToList(),
            invites.Select(ToInvite).ToList(),
            notices.Select(ToNotice).ToList(),
            pendingDto);
    }

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

        var link = $"{PublicWebBase()}/app/grupo/convite/{invite.Token}";
        try
        {
            await _email.SendAsync(
                email,
                $"{me.Name} convidou você para o grupo no Conora",
                new EmailContent(
                    $"{me.Name} convidou você",
                    [
                        $"{me.Name} convidou você para compartilhar o orçamento familiar no Conora.",
                        "Abra o link, entre se já tiver conta ou crie com este mesmo e-mail."
                    ])
                {
                    Preheader = "Convite para o grupo familiar no Conora.",
                    ActionLabel = "Aceitar convite",
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
            }
            else
            {
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
        await _categories.EnsureDefaultsAsync(ct);
        await _categories.MergeOnJoinAsync(group.BudgetOwnerUsuarioId, self, ct);
        return await GetGroupAsync(ct);
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

        await RemoveMemberCoreAsync(membership.FamilyGroupId, self, self, markPersonal: true, ct);
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

    public async Task<FamilyOwnerTransferResponse> RequestOwnerTransferAsync(
        FamilyOwnerTransferRequest request,
        CancellationToken ct)
    {
        var self = RequireUserId();
        var (group, isOwner) = await RequireGroupAsync(ct);
        if (!isOwner)
            throw new ForbiddenException("Só o responsável pelo orçamento pode transferir.");

        if (await _family.FindOpenOwnerTransferAsync(group.Id, ct) is not null)
            throw new ValidationException("transfer", "Já existe uma transferência aberta.");

        var members = await _family.ListActiveMembersAsync(group.Id, ct);
        if (members.All(m => m.UsuarioId != request.ToUsuarioId))
            throw new ValidationException("toUsuarioId", "Escolha um membro do grupo.");

        var transfer = FamilyOwnerTransfer.Create(group.Id, self, request.ToUsuarioId, OwnerTransferTtl);
        _family.AddOwnerTransfer(transfer);

        var from = await RequireUserAsync(ct);
        var to = await _family.FindUserByIdAsync(request.ToUsuarioId, ct)
                 ?? throw new NotFoundException("Usuário", request.ToUsuarioId);

        var link = $"{PublicWebBase()}/app/grupo/owner/{transfer.Token}";
        try
        {
            await _email.SendAsync(
                to.Email,
                $"{from.Name} quer transferir o orçamento familiar no Conora",
                new EmailContent(
                    "Transferência do orçamento familiar",
                    [
                        $"{from.Name} pediu que você assuma o orçamento do grupo familiar no Conora.",
                        "Ao aceitar, você passa a ser o único que edita o orçamento familiar e recebe as linhas de planejamento atuais."
                    ])
                {
                    Preheader = "Pedido de transferência do orçamento familiar.",
                    ActionLabel = "Aceitar transferência",
                    ActionUrl = link,
                    Note = $"O pedido vale até {transfer.ExpiresAt:dd/MM/yyyy HH:mm} UTC."
                },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Owner-transfer e-mail failed for {Email}; transfer kept.", to.Email);
        }

        _family.AddNotice(FamilyNotice.Create(
            to.Id,
            "OwnerTransferRequested",
            $"{from.Name} pediu que você assuma o orçamento familiar."));

        AuditRecorder.Record(_audits, _correlation, "FamilyOwnerTransfer", transfer.Id, "OwnerTransferRequested",
            new { transfer.ToUsuarioId });
        await _uow.SaveChangesAsync(ct);

        return new FamilyOwnerTransferResponse(
            transfer.Id, self, from.Name, to.Id, to.Name, transfer.ExpiresAt, true, false);
    }

    public async Task CancelOwnerTransferAsync(CancellationToken ct)
    {
        var (group, isOwner) = await RequireGroupAsync(ct);
        if (!isOwner)
            throw new ForbiddenException("Só o responsável pode cancelar a transferência.");
        var pending = await _family.FindOpenOwnerTransferAsync(group.Id, ct, track: true)
                      ?? throw new NotFoundException("Transferência", group.Id);
        pending.Cancel();
        AuditRecorder.Record(_audits, _correlation, "FamilyOwnerTransfer", pending.Id, "OwnerTransferCancelled", new { });
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<FamilyGroupResponse> AcceptOwnerTransferAsync(string token, CancellationToken ct)
    {
        await _uow.ExecuteInTransactionAsync(innerCt => CompleteOwnerTransferAsync(token, innerCt), ct);
        return await GetGroupAsync(ct);
    }

    private async Task CompleteOwnerTransferAsync(string token, CancellationToken ct)
    {
        var self = RequireUserId();
        var transfer = await _family.FindOwnerTransferByTokenAsync(token, ct, track: true)
                       ?? throw new ValidationException("token", "Transferência inválida.");
        var wasOpen = transfer.IsOpen;
        transfer.Accept(self);

        var group = await _family.GetGroupAsync(transfer.FamilyGroupId, ct)
                    ?? throw new ValidationException("token", "Grupo não encontrado.");
        var members = await _family.ListActiveMembersAsync(group.Id, ct);
        if (members.All(m => m.UsuarioId != self))
            throw new ValidationException("token", "Você não faz mais parte deste grupo.");

        if (!group.IsBudgetOwner(self))
        {
            if (!group.IsBudgetOwner(transfer.FromUsuarioId))
                throw new ValidationException("token", "O solicitante não é mais o responsável.");

            group.TransferBudgetOwner(self);
            await CopyOwnerBudgetsAsync(transfer.FromUsuarioId, self, ct);
            await _repo.ReassignCategoryCreatorAsync(transfer.FromUsuarioId, self, ct);

            var openInvites = await _family.ListOpenInvitesByGroupAsync(group.Id, ct);
            foreach (var invite in openInvites)
                invite.ReassignInviter(self);
        }

        if (wasOpen)
        {
            _family.AddNotice(FamilyNotice.Create(
                transfer.FromUsuarioId,
                "OwnerTransferAccepted",
                "A transferência do orçamento familiar foi aceita."));

            AuditRecorder.Record(_audits, _correlation, "FamilyOwnerTransfer", transfer.Id, "OwnerTransferAccepted",
                new { transfer.FromUsuarioId, to = self });
        }

        await _uow.SaveChangesAsync(ct);
    }

    public async Task MarkNoticeReadAsync(Guid noticeId, CancellationToken ct)
    {
        var self = RequireUserId();
        var notice = await _family.GetNoticeAsync(noticeId, self, ct)
                     ?? throw new NotFoundException("Aviso", noticeId);
        notice.MarkRead();
        await _uow.SaveChangesAsync(ct);
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

    private async Task CopyOwnerBudgetsAsync(Guid fromUsuarioId, Guid toUsuarioId, CancellationToken ct)
    {
        var fromBudgets = await _repo.ListForUsuariosAsync<Budget>([fromUsuarioId], _ => true, ct);
        if (fromBudgets.Count == 0)
            return;

        var toCategories = (await _repo.ListForUsuariosAsync<Category>(
                [toUsuarioId], c => c.Level == CategoryLevel.Analytical, ct))
            .ToList();
        var toByCode = toCategories
            .Where(c => !string.IsNullOrWhiteSpace(c.Code))
            .GroupBy(c => c.Code!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var toByFamilyKey = toCategories
            .Where(c => c.FamilyKey is not null)
            .GroupBy(c => c.FamilyKey!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var toByName = toCategories
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var stagedLines = new Dictionary<(Guid BudgetId, Guid CategoryId), BudgetLine>();

        foreach (var fromBudget in fromBudgets)
        {
            var lines = await _repo.ListForUsuariosAsync<BudgetLine>(
                [fromUsuarioId], l => l.BudgetId == fromBudget.Id, ct);
            if (lines.Count == 0)
                continue;

            var toBudget = await _repo.FirstOrDefaultAsync<Budget>(
                b => b.CompetenceYm == fromBudget.CompetenceYm, ct, track: true);
            if (toBudget is null)
            {
                toBudget = Budget.Create(fromBudget.CompetenceYm, fromBudget.Mode);
                _repo.Add(toBudget);
            }
            else
            {
                toBudget.SetMode(fromBudget.Mode);
            }

            var fromCategories = await _repo.ListForUsuariosAsync<Category>(
                [fromUsuarioId], c => lines.Select(l => l.CategoryId).Contains(c.Id), ct);

            foreach (var line in lines)
            {
                var fromCat = fromCategories.FirstOrDefault(c => c.Id == line.CategoryId);
                if (fromCat is null)
                    continue;

                Category? toCat = null;
                if (!string.IsNullOrWhiteSpace(fromCat.Code))
                    toByCode.TryGetValue(fromCat.Code, out toCat);
                if (toCat is null && fromCat.FamilyKey is Guid familyKey)
                    toByFamilyKey.TryGetValue(familyKey, out toCat);
                toCat ??= toByName.GetValueOrDefault(fromCat.Name);
                if (toCat is null)
                    continue;

                var key = (toBudget.Id, toCat.Id);
                if (stagedLines.TryGetValue(key, out var staged))
                {
                    staged.SetPlanned(line.PlannedAmount);
                    staged.SetExpenseDay(line.ExpenseDay);
                    continue;
                }

                var existing = await _repo.FirstOrDefaultAsync<BudgetLine>(
                    l => l.BudgetId == toBudget.Id && l.CategoryId == toCat.Id, ct, track: true);
                if (existing is null)
                {
                    var created = BudgetLine.Create(toBudget.Id, toCat.Id, line.PlannedAmount, line.ExpenseDay);
                    _repo.Add(created);
                    stagedLines[key] = created;
                }
                else
                {
                    existing.SetPlanned(line.PlannedAmount);
                    existing.SetExpenseDay(line.ExpenseDay);
                    stagedLines[key] = existing;
                }
            }
        }
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

    private static ProfileResponse ToProfile(User user)
        => new(
            user.Id,
            user.Name,
            user.Email,
            user.OnboardingCompleted,
            user.ThemeId,
            user.QuietMode,
            user.EmailsEnabled,
            user.ReserveTargetMonths,
            user.MonthCloseDay,
            user.IncludeAbsoluteValuesInRetrospective,
            user.VoiceEntryConfirm);

    private static FamilyInviteResponse ToInvite(FamilyInvite i)
        => new(i.Id, i.Email, i.ExpiresAt, i.AcceptedAt, i.CancelledAt, i.IsOpen);

    private static FamilyNoticeResponse ToNotice(FamilyNotice n)
        => new(n.Id, n.Kind, n.Message, n.CreatedAt, n.ReadAt is not null);
}
