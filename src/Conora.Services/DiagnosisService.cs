using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

/// <summary>
/// Income diagnosis (spec v1.1 §3). Net spendable = gross - INSS - IR. The tithe never reduces income:
/// it only becomes a planned expense in Contribuições/Doações.
/// </summary>
public sealed class DiagnosisService
{
    private readonly IFinanceRepository _repo;
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ICorrelationContext _correlation;
    private readonly PlanService _plan;
    private readonly MonthService _months;
    private readonly BudgetService _budgets;

    public DiagnosisService(
        IFinanceRepository repo,
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        PlanService plan,
        MonthService months,
        BudgetService budgets)
    {
        _repo = repo;
        _audits = audits;
        _uow = uow;
        _correlation = correlation;
        _plan = plan;
        _months = months;
        _budgets = budgets;
    }

    public async Task<DiagnosisSummaryResponse> GetAsync(string competenceYm, CancellationToken ct)
    {
        var ym = Competence.Require(competenceYm);
        var sources = await _repo.ListAsync<IncomeSource>(s => s.CompetenceYm == ym, ct);
        return BuildSummary(ym, sources);
    }

    public async Task<IncomeSourceResponse> CreateAsync(IncomeSourceRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var source = IncomeSource.Create(request.Name, request.CompetenceYm, request.Gross, request.Inss, request.Ir, request.Tithe);
        await _months.EnsureOpenAsync(source.CompetenceYm, ct);

        var existing = await _repo.ListAsync<IncomeSource>(s => s.CompetenceYm == source.CompetenceYm, ct);
        _repo.Add(source);
        await _budgets.SyncPlannedTitheAsync(source.CompetenceYm, existing.Sum(s => s.Tithe) + source.Tithe, ct);

        AuditRecorder.Record(_audits, _correlation, "IncomeSource", source.Id, "IncomeSourceCreated",
            new { source.CompetenceYm, source.NetSpendable, source.Tithe });
        await _uow.SaveChangesAsync(ct);
        return ToResponse(source);
    }

    public async Task<IncomeSourceResponse> UpdateAsync(Guid id, UpdateIncomeSourceRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var source = await _repo.GetAsync<IncomeSource>(id, ct) ?? throw new NotFoundException("Fonte de renda", id);
        await _months.EnsureOpenAsync(source.CompetenceYm, ct);

        source.Update(request.Name, request.Gross, request.Inss, request.Ir, request.Tithe);
        await SyncTitheAsync(source.CompetenceYm, ct);

        AuditRecorder.Record(_audits, _correlation, "IncomeSource", source.Id, "IncomeSourceUpdated",
            new { source.CompetenceYm, source.NetSpendable, source.Tithe });
        await _uow.SaveChangesAsync(ct);
        return ToResponse(source);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var source = await _repo.GetAsync<IncomeSource>(id, ct) ?? throw new NotFoundException("Fonte de renda", id);
        await _months.EnsureOpenAsync(source.CompetenceYm, ct);

        if (await _repo.AnyAsync<Entry>(e => e.IncomeSourceId == id, ct))
            throw new ValidationException("id", "A fonte tem receita lançada. Exclua o lançamento primeiro.");

        _repo.SoftDelete(source);
        await SyncTitheAsync(source.CompetenceYm, ct);
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>Suggested tithe (10% of net spendable) for the UI; it is only a suggestion.</summary>
    public static decimal SuggestTithe(decimal gross, decimal inss, decimal ir)
        => IncomeDiagnosisCalculator.SuggestTithe(IncomeDiagnosisCalculator.Calculate(gross, inss, ir, 0).NetSpendable);

    private async Task SyncTitheAsync(string ym, CancellationToken ct)
    {
        // Tracked entities already carry the pending changes (update/soft delete) even before saving.
        var sources = await _repo.ListAsync<IncomeSource>(s => s.CompetenceYm == ym, ct, track: true);
        var total = sources.Where(s => s.DeletedAt == null).Sum(s => s.Tithe);
        await _budgets.SyncPlannedTitheAsync(ym, total, ct);
    }

    private static DiagnosisSummaryResponse BuildSummary(string ym, IReadOnlyList<IncomeSource> sources)
        => new(
            ym,
            sources.Sum(s => s.Gross),
            sources.Sum(s => s.Inss),
            sources.Sum(s => s.Ir),
            sources.Sum(s => s.NetSpendable),
            sources.Sum(s => s.Tithe),
            sources.OrderBy(s => s.Name).Select(ToResponse).ToList());

    private static IncomeSourceResponse ToResponse(IncomeSource s)
        => new(s.Id, s.Name, s.CompetenceYm, s.Gross, s.Inss, s.Ir, s.Tithe, s.NetSpendable);
}
