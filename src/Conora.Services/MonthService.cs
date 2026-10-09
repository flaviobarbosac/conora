using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class MonthService
{
    private readonly IFinanceRepository _repo;
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenant;
    private readonly ICorrelationContext _correlation;
    private readonly PlanService _plan;

    public MonthService(
        IFinanceRepository repo,
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ITenantContext tenant,
        ICorrelationContext correlation,
        PlanService plan)
    {
        _repo = repo;
        _audits = audits;
        _uow = uow;
        _tenant = tenant;
        _correlation = correlation;
        _plan = plan;
    }

    public async Task EnsureOpenAsync(string competenceYm, CancellationToken ct)
    {
        Competence.Require(competenceYm);
        if (await _repo.AnyAsync<MonthLock>(m => m.CompetenceYm == competenceYm && m.IsClosed, ct))
            throw new MonthClosedException(competenceYm);
    }

    public async Task EnsureOpenAsync(IEnumerable<string> competences, CancellationToken ct)
    {
        foreach (var ym in competences.Distinct())
            await EnsureOpenAsync(ym, ct);
    }

    public async Task<MonthStatusResponse> GetAsync(string competenceYm, CancellationToken ct)
    {
        Competence.Require(competenceYm);
        var row = await _repo.FirstOrDefaultAsync<MonthLock>(m => m.CompetenceYm == competenceYm, ct, track: false);
        return ToResponse(competenceYm, row);
    }

    public async Task<IReadOnlyList<MonthStatusResponse>> ListAsync(CancellationToken ct)
    {
        var rows = await _repo.ListAsync<MonthLock>(null, ct);
        return rows.OrderByDescending(r => r.CompetenceYm).Select(r => ToResponse(r.CompetenceYm, r)).ToList();
    }

    public async Task<MonthStatusResponse> CloseAsync(string competenceYm, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        Competence.Require(competenceYm);

        var row = await _repo.FirstOrDefaultAsync<MonthLock>(m => m.CompetenceYm == competenceYm, ct);
        if (row is { IsClosed: true })
            throw new ValidationException("competenceYm", "O mês já está fechado.");

        var owner = RequireOwner();
        if (row is null)
        {
            row = MonthLock.Close(competenceYm, owner);
            _repo.Add(row);
        }
        else
        {
            row.CloseMonth(owner);
        }

        AuditRecorder.Record(_audits, _correlation, "MonthLock", row.Id, "MonthClosed", new { competenceYm });
        await _uow.SaveChangesAsync(ct);
        return ToResponse(competenceYm, row);
    }

    /// <summary>Reopening requires a reason, is audited, and only the owner who closed the month can do it.</summary>
    public async Task<MonthStatusResponse> ReopenAsync(string competenceYm, ReopenMonthRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        Competence.Require(competenceYm);

        var row = await _repo.FirstOrDefaultAsync<MonthLock>(m => m.CompetenceYm == competenceYm, ct);
        if (row is not { IsClosed: true })
            throw new ValidationException("competenceYm", "O mês não está fechado.");

        row.Reopen(request.Reason, RequireOwner());
        AuditRecorder.Record(_audits, _correlation, "MonthLock", row.Id, "MonthReopened", new { competenceYm, reason = row.ReopenReason });
        await _uow.SaveChangesAsync(ct);
        return ToResponse(competenceYm, row);
    }

    private Guid RequireOwner()
        => _tenant.UsuarioId ?? throw new ForbiddenException("Somente o titular pode fechar ou reabrir o mês.");

    private static MonthStatusResponse ToResponse(string ym, MonthLock? row)
        => row is null
            ? new MonthStatusResponse(ym, false, null, null, null)
            : new MonthStatusResponse(ym, row.IsClosed, row.ClosedAt, row.ReopenReason, row.ClosedByUsuarioId);
}
