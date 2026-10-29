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
    private readonly IStoreBillingGateway _storeBilling;

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
        PlanService plan,
        IStoreBillingGateway storeBilling)
    {
        _plan = plan;
        _storeBilling = storeBilling;
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

    public Task<User?> FindUserByIdAsync(Guid id, CancellationToken ct = default, bool track = false)
        => _family.FindUserByIdAsync(id, ct, track);

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
                pending.ToUsuarioId == self,
                pending.AuthorizedAt is not null,
                $"{PublicWebBase()}/app/grupo/owner/{pending.Token}");
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


















    /// <summary>
    /// At store cancellation: from the handover month forward, her planned budgets are removed
    /// and replaced by every planned month he had. Earlier months stay on each side.
    /// </summary>



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
            user.VoiceEntryConfirm,
            !string.IsNullOrEmpty(user.AppleId),
            user.BudgetAlertPercent,
            user.PhotoEvolutionEnabled,
            user.Phone,
            user.Cep,
            user.ContractAcknowledgedAt);

    private static FamilyInviteResponse ToInvite(FamilyInvite i)
        => new(i.Id, i.Email, i.ExpiresAt, i.AcceptedAt, i.CancelledAt, i.IsOpen);

    private static FamilyNoticeResponse ToNotice(FamilyNotice n)
        => new(n.Id, n.Kind, n.Message, n.CreatedAt, n.ReadAt is not null);
}

