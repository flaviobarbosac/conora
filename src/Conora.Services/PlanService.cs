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
        var familyMemberWithoutCover = false;
        if (covering is null)
        {
            var (familyPlan, inGroupAsNonOwner) = await TryFamilyOwnerPlanAsync(self, now, ct);
            if (familyPlan is not null)
            {
                covering = familyPlan;
                coveredByFamily = true;
            }
            else if (inGroupAsNonOwner)
            {
                // Owner stopped paying (or has no covering plan): whole group falls to free / read-only.
                familyMemberWithoutCover = true;
            }
        }
        covering ??= own;

        var user = await _family.FindUserByIdAsync(self, ct);
        var state = AccessPolicy.Evaluate(
            user?.CreatedAt ?? now,
            covering,
            now,
            user?.IsTrainingStudent ?? true);
        if (familyMemberWithoutCover && state.IsWritable)
            state = new AccessState(AccessPhase.Grace, state.TrialEndsAt, state.BlockAt);

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
                    "O Conora está em modo somente leitura (plano gratuito / consulta). " +
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
                "O plano Individual não inclui grupo familiar. Mude para o Duo ou Family para convidar pessoas.");

        var max = PlanCatalog.MaxExtraMembers(tier);
        if (max is not null && currentExtraMembers >= max)
        {
            var label = tier == PlanTier.Family ? "Duo" : "Family";
            throw new ValidationException(
                "email",
                $"Seu plano {label} permite até {max} pessoa(s) além de você.");
        }
    }

    /// <summary>
    /// Activates or renews only when the store confirmed the purchase (token from Play / App Store webhook or stub).
    /// </summary>
    public async Task<PlanResponse> SubscribeAsync(SubscribeRequest request, CancellationToken ct)
    {
        if (!PlanCatalog.IsSellable(request.Plan))
            throw new ValidationException("plan", "Plano inválido.");
        if (string.IsNullOrWhiteSpace(request.StorePurchaseToken))
            throw new ValidationException(
                "store",
                "A assinatura só muda quando a Google Play ou a App Store confirmar a compra.");

        return await ApplyStorePlanAsync(request.Plan, request.StorePurchaseToken.Trim(), "client", ct);
    }

    /// <summary>Called by store webhooks after Play / App Store notifies a purchase or renewal.</summary>
    public async Task ApplyStoreConfirmationAsync(StorePurchaseConfirmation confirmation, CancellationToken ct)
    {
        if (!PlanCatalog.IsSellable(confirmation.Plan))
            throw new ValidationException("plan", "Plano inválido.");
        if (string.IsNullOrWhiteSpace(confirmation.PurchaseToken))
            throw new ValidationException("store", "Token da loja obrigatório.");

        _tenant.Set(confirmation.UsuarioId);
        await ApplyStorePlanAsync(confirmation.Plan, confirmation.PurchaseToken.Trim(), confirmation.Store, ct);
    }

    private async Task<PlanResponse> ApplyStorePlanAsync(PlanKind plan, string purchaseToken, string store, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var sub = await _repo.FirstOrDefaultAsync<WorkspaceSubscription>(_ => true, ct);
        if (sub is null)
        {
            sub = WorkspaceSubscription.Activate(plan, now);
            _repo.Add(sub);
        }
        else
        {
            sub.Renew(plan, now);
        }

        AuditRecorder.Record(
            _audits,
            _correlation,
            "WorkspaceSubscription",
            sub.Id,
            "PlanSubscribedFromStore",
            new { plan, store, purchaseToken, sub.ExpiresAt });
        await _uow.SaveChangesAsync(ct);

        _snapshot = null;
        return await GetAsync(ct);
    }

    /// <returns>Covering family plan when writable; inGroupAsNonOwner true when the user is a member under another budget owner.</returns>
    private async Task<(WorkspaceSubscription? Plan, bool InGroupAsNonOwner)> TryFamilyOwnerPlanAsync(
        Guid self, DateTime now, CancellationToken ct)
    {
        var membership = await _family.FindActiveMembershipAsync(self, ct);
        if (membership is null)
            return (null, false);
        var group = await _family.GetGroupAsync(membership.FamilyGroupId, ct, track: false);
        if (group is null || group.BudgetOwnerUsuarioId == self)
            return (null, false);
        var ownerPlan = await _family.FindSubscriptionAsync(group.BudgetOwnerUsuarioId, ct);
        if (ownerPlan is not null && ownerPlan.IsWritable(now) && ownerPlan.Tier != PlanTier.Individual)
            return (ownerPlan, true);
        return (null, true);
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
