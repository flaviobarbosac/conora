using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class PlanService
{
    private readonly IFinanceRepository _repo;
    private readonly IFamilyGroupRepository _family;
    private readonly ITenantContext _tenant;
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ICorrelationContext _correlation;

    // Scoped service: the middleware and the write guards share one evaluation per request.
    private AccessSnapshot? _snapshot;

    public PlanService(
        IFinanceRepository repo,
        IFamilyGroupRepository family,
        ITenantContext tenant,
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ICorrelationContext correlation)
    {
        _repo = repo;
        _family = family;
        _tenant = tenant;
        _audits = audits;
        _uow = uow;
        _correlation = correlation;
    }

    public async Task<PlanResponse> GetAsync(CancellationToken ct)
        => ToResponse(await GetAccessAsync(ct), DateTime.UtcNow);

    /// <summary>Current access phase (trial, subscribed, grace, expired or blocked).</summary>
    public async Task<AccessSnapshot> GetAccessAsync(CancellationToken ct)
    {
        if (_snapshot is not null)
            return _snapshot;

        var self = _tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");
        var now = DateTime.UtcNow;
        var own = await _repo.FirstOrDefaultAsync<WorkspaceSubscription>(_ => true, ct, track: false);

        WorkspaceSubscription? covering = own is not null && own.IsWritable(now) ? own : null;
        var coveredByFamily = false;
        if (covering is null)
        {
            var familyPlan = await FindFamilyOwnerPlanAsync(self, now, ct);
            if (familyPlan is not null)
            {
                covering = familyPlan;
                coveredByFamily = true;
            }
        }
        covering ??= own;

        var user = await _family.FindUserByIdAsync(self, ct);
        var state = AccessPolicy.Evaluate(user?.CreatedAt ?? now, covering, now);
        _snapshot = new AccessSnapshot(state, covering, coveredByFamily);
        return _snapshot;
    }

    /// <summary>Blocks writes outside trial / active plan (grace, expired, blocked).</summary>
    public async Task EnsureWritableAsync(CancellationToken ct)
    {
        var access = await GetAccessAsync(ct);
        switch (access.State.Phase)
        {
            case AccessPhase.Trial or AccessPhase.Subscribed:
                return;
            case AccessPhase.Blocked:
                throw new PlanBlockedException();
            case AccessPhase.Grace:
                throw new PlanReadOnlyException(
                    "Seu acesso gratuito de 90 dias terminou: o Conora está em modo somente leitura. " +
                    "Você ainda pode consultar e exportar seus dados. Assine um plano para voltar a lançar.");
            default:
                throw new PlanReadOnlyException();
        }
    }

    /// <summary>
    /// Validates that the plan allows one more family member. Trial allows everything;
    /// Individual has no family group; Family allows the owner plus 2 people.
    /// </summary>
    public async Task EnsureCanAddFamilyMemberAsync(int currentExtraMembers, CancellationToken ct)
    {
        await EnsureWritableAsync(ct);
        var access = await GetAccessAsync(ct);
        if (access.State.Phase != AccessPhase.Subscribed || access.Subscription is null)
            return;

        var tier = access.Subscription.Tier;
        if (tier == PlanTier.Individual)
            throw new ForbiddenException(
                "O plano Individual não inclui grupo familiar. Mude para o plano Família para convidar pessoas.");

        var max = PlanCatalog.MaxExtraMembers(tier);
        if (max is not null && currentExtraMembers >= max)
            throw new ValidationException(
                "email",
                $"Seu plano Família permite até {max} pessoas além de você. Mude para o Família+ para adicionar mais.");
    }

    /// <summary>Activates or renews the plan. Payment gateway integration is not part of this version.</summary>
    public async Task<PlanResponse> SubscribeAsync(SubscribeRequest request, CancellationToken ct)
    {
        if (!PlanCatalog.IsSellable(request.Plan))
            throw new ValidationException("plan", "Plano inválido.");

        var now = DateTime.UtcNow;
        var sub = await _repo.FirstOrDefaultAsync<WorkspaceSubscription>(_ => true, ct);
        if (sub is null)
        {
            sub = WorkspaceSubscription.Activate(request.Plan, now);
            _repo.Add(sub);
        }
        else
        {
            sub.Renew(request.Plan, now);
        }

        AuditRecorder.Record(_audits, _correlation, "WorkspaceSubscription", sub.Id, "PlanSubscribed", new { request.Plan, sub.ExpiresAt });
        await _uow.SaveChangesAsync(ct);

        _snapshot = null;
        return await GetAsync(ct);
    }

    private async Task<WorkspaceSubscription?> FindFamilyOwnerPlanAsync(Guid self, DateTime now, CancellationToken ct)
    {
        var membership = await _family.FindActiveMembershipAsync(self, ct);
        if (membership is null)
            return null;
        var group = await _family.GetGroupAsync(membership.FamilyGroupId, ct, track: false);
        if (group is null || group.BudgetOwnerUsuarioId == self)
            return null;
        var ownerPlan = await _family.FindSubscriptionAsync(group.BudgetOwnerUsuarioId, ct);
        return ownerPlan is not null && ownerPlan.IsWritable(now) && ownerPlan.Tier != PlanTier.Individual
            ? ownerPlan
            : null;
    }

    private static PlanResponse ToResponse(AccessSnapshot access, DateTime now)
    {
        var sub = access.Subscription;
        var phase = access.State.Phase;
        var daysLeft = phase switch
        {
            AccessPhase.Trial => AccessPolicy.DaysUntil(access.State.TrialEndsAt, now),
            AccessPhase.Grace => AccessPolicy.DaysUntil(access.State.BlockAt, now),
            AccessPhase.Subscribed when sub is not null => AccessPolicy.DaysUntil(sub.ExpiresAt, now),
            _ => 0
        };
        var offers = PlanCatalog.Offers
            .Select(o => new PlanOfferResponse(o.Kind, o.Tier, o.Yearly, o.Price, o.MonthlyEquivalent, PlanCatalog.MaxExtraMembers(o.Tier)))
            .ToList();

        return new PlanResponse(
            sub?.Plan,
            sub?.Tier,
            sub is not null && PlanCatalog.IsYearly(sub.Plan),
            sub?.EffectiveStatus(now) ?? SubscriptionStatus.Active,
            sub?.ExpiresAt,
            !access.State.IsWritable,
            sub is null ? null : WorkspaceSubscription.PriceOf(sub.Plan),
            phase,
            access.State.TrialEndsAt,
            access.State.BlockAt,
            daysLeft,
            access.CoveredByFamily,
            offers);
    }
}

public sealed record AccessSnapshot(AccessState State, WorkspaceSubscription? Subscription, bool CoveredByFamily);
