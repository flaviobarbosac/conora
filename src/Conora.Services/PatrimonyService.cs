using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class PatrimonyService
{
    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly PlanService _plan;

    public PatrimonyService(IFinanceRepository repo, IUnitOfWork uow, PlanService plan)
    {
        _repo = repo;
        _uow = uow;
        _plan = plan;
    }

    public async Task<PatrimonySummaryResponse> GetSummaryAsync(CancellationToken ct)
    {
        var items = await _repo.ListAsync<PatrimonyItem>(null, ct);
        var accounts = await _repo.ListAsync<Account>(a => !a.IsArchived, ct);
        var unpaid = await _repo.SumAsync<CardInvoice>(i => i.Status != InvoiceStatus.Paid, i => i.Total, ct);

        var accountsBalance = accounts.Sum(a => a.Balance);
        var assets = items.Where(i => i.Kind == PatrimonyKind.Asset).Sum(i => i.Amount) + accountsBalance;
        var liabilities = items.Where(i => i.Kind == PatrimonyKind.Liability).Sum(i => i.Amount) + unpaid;

        return new PatrimonySummaryResponse(
            accountsBalance,
            assets,
            unpaid,
            liabilities,
            assets - liabilities,
            items.OrderBy(i => i.Kind).ThenBy(i => i.Name).Select(ToResponse).ToList());
    }

    public async Task<PatrimonyItemResponse> CreateAsync(PatrimonyItemRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var item = PatrimonyItem.Create(request.Kind, request.Name, request.Amount);
        _repo.Add(item);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(item);
    }

    public async Task<PatrimonyItemResponse> UpdateAsync(Guid id, UpdatePatrimonyItemRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var item = await _repo.GetAsync<PatrimonyItem>(id, ct) ?? throw new NotFoundException("Item de patrimônio", id);
        item.Update(request.Name, request.Amount);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(item);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var item = await _repo.GetAsync<PatrimonyItem>(id, ct) ?? throw new NotFoundException("Item de patrimônio", id);
        _repo.SoftDelete(item);
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Reserve base (spec v1.1 §2): average of the last 3 months of essential spending; with fewer months uses
    /// the ones available; with no history uses the essential budget of the reference month.
    /// </summary>
    public async Task<ReserveResponse> GetReserveAsync(string? referenceYm, CancellationToken ct)
    {
        var reference = string.IsNullOrWhiteSpace(referenceYm) ? Competence.From(DateTime.UtcNow) : Competence.Require(referenceYm);
        var essentialIds = (await _repo.ListAsync<Category>(c => c.IsEssential, ct)).Select(c => c.Id).ToList();
        if (essentialIds.Count == 0)
            return new ReserveResponse(reference, 0, 0, "None");

        var monthly = new List<decimal>();
        for (var back = 1; back <= 3; back++)
        {
            var ym = Competence.AddMonths(reference, -back);
            var spent = await _repo.SumAsync<Entry>(
                e => e.CompetenceYm == ym
                     && (e.Type == EntryType.Expense || e.Type == EntryType.Contribution)
                     && e.CategoryId != null && essentialIds.Contains(e.CategoryId.Value),
                e => e.Amount, ct);
            spent += await _repo.SumAsync<CardPurchase>(
                p => p.CompetenceYm == ym && essentialIds.Contains(p.CategoryId), p => p.Amount, ct);

            if (spent > 0)
                monthly.Add(spent);
        }

        if (monthly.Count > 0)
            return new ReserveResponse(reference, decimal.Round(monthly.Average(), 2), monthly.Count,
                monthly.Count == 3 ? "Last3Months" : "AvailableMonths");

        var budget = await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == reference, ct, track: false);
        if (budget is null)
            return new ReserveResponse(reference, 0, 0, "None");

        var planned = await _repo.SumAsync<BudgetLine>(
            l => l.BudgetId == budget.Id && essentialIds.Contains(l.CategoryId), l => l.PlannedAmount, ct);
        return planned > 0
            ? new ReserveResponse(reference, planned, 0, "Budget")
            : new ReserveResponse(reference, 0, 0, "None");
    }

    private static PatrimonyItemResponse ToResponse(PatrimonyItem i) => new(i.Id, i.Kind, i.Name, i.Amount);
}
