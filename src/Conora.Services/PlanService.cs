using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class PlanService
{
    private readonly IFinanceRepository _repo;
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ICorrelationContext _correlation;

    public PlanService(
        IFinanceRepository repo,
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ICorrelationContext correlation)
    {
        _repo = repo;
        _audits = audits;
        _uow = uow;
        _correlation = correlation;
    }

    public async Task<PlanResponse> GetAsync(CancellationToken ct)
    {
        var sub = await _repo.FirstOrDefaultAsync<WorkspaceSubscription>(_ => true, ct, track: false);
        return ToResponse(sub, DateTime.UtcNow);
    }

    /// <summary>Blocks writes when the subscription is expired/read-only. No subscription yet = trial (writable).</summary>
    public async Task EnsureWritableAsync(CancellationToken ct)
    {
        var sub = await _repo.FirstOrDefaultAsync<WorkspaceSubscription>(_ => true, ct, track: false);
        if (sub is not null && !sub.IsWritable(DateTime.UtcNow))
            throw new PlanReadOnlyException();
    }

    /// <summary>Activates or renews the plan. Payment gateway integration is not part of this version.</summary>
    public async Task<PlanResponse> SubscribeAsync(SubscribeRequest request, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.Plan))
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
        return ToResponse(sub, now);
    }

    private static PlanResponse ToResponse(WorkspaceSubscription? sub, DateTime now)
    {
        if (sub is null)
            return new PlanResponse(null, SubscriptionStatus.Active, null, false, null);

        var status = sub.EffectiveStatus(now);
        return new PlanResponse(sub.Plan, status, sub.ExpiresAt, status != SubscriptionStatus.Active, WorkspaceSubscription.PriceOf(sub.Plan));
    }
}
