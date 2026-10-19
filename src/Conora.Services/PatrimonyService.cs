using Conora.Domain.Catalog;
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
    private readonly CategoryService _categories;

    public PatrimonyService(IFinanceRepository repo, IUnitOfWork uow, PlanService plan, CategoryService categories)
    {
        _repo = repo;
        _uow = uow;
        _plan = plan;
        _categories = categories;
    }

    public async Task<PatrimonySummaryResponse> GetSummaryAsync(CancellationToken ct)
    {
        await _categories.EnsureDefaultsAsync(ct);
        var items = await _repo.ListAsync<PatrimonyItem>(null, ct);
        var accounts = (await _categories.ListAsync(null, true, false, ct)).ToDictionary(c => c.Id);
        var bankAccounts = await _repo.ListAsync<Account>(a => !a.IsArchived, ct);
        var unpaid = await _repo.SumAsync<CardInvoice>(i => i.Status != InvoiceStatus.Paid, i => i.Total, ct);

        var accountsBalance = bankAccounts.Sum(a => a.Balance);
        var responses = items
            .Select(i => ToResponse(i, accounts))
            .OrderBy(i => i.Section)
            .ThenBy(i => i.GroupName)
            .ThenBy(i => i.CategoryName)
            .ThenBy(i => i.Name)
            .ToList();

        var assetsInUse = responses.Where(i => i.Section == CategorySection.Asset
            && i.GroupName.Equals(SystemCategories.AssetsInUse, StringComparison.OrdinalIgnoreCase)).Sum(i => i.Amount);
        var assetsNotInUse = responses.Where(i => i.Section == CategorySection.Asset
            && i.GroupName.Equals(SystemCategories.AssetsNotInUse, StringComparison.OrdinalIgnoreCase)).Sum(i => i.Amount);
        var assets = responses.Where(i => i.Section == CategorySection.Asset).Sum(i => i.Amount);
        var liabilities = responses.Where(i => i.Section == CategorySection.Liability).Sum(i => i.Amount);

        var groups = responses
            .GroupBy(i => (i.Section, i.GroupName))
            .Select(g => new PatrimonyGroupTotal(g.Key.GroupName, g.Key.Section, g.Sum(x => x.Amount)))
            .OrderBy(g => g.Section)
            .ThenBy(g => g.GroupName)
            .ToList();

        return new PatrimonySummaryResponse(
            accountsBalance,
            assets,
            assetsInUse,
            assetsNotInUse,
            unpaid,
            liabilities,
            assets - liabilities,
            groups,
            responses);
    }

    public async Task<PatrimonyItemResponse> CreateAsync(PatrimonyItemRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var account = await _categories.RequireAnalyticalAsync(request.CategoryId, ct);
        if (!SystemCategories.IsPatrimonySection(account.Section))
            throw new ValidationException("categoryId", "Patrimônio só aceita contas de Ativo ou Passivo.");

        var item = PatrimonyItem.Create(account.Id, request.Name, request.Amount);
        _repo.Add(item);
        await _uow.SaveChangesAsync(ct);

        var accounts = (await _categories.ListAsync(null, true, false, ct)).ToDictionary(c => c.Id);
        return ToResponse(item, accounts);
    }

    public async Task<PatrimonyItemResponse> UpdateAsync(Guid id, UpdatePatrimonyItemRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var item = await _repo.GetAsync<PatrimonyItem>(id, ct) ?? throw new NotFoundException("Item de patrimônio", id);
        var account = await _categories.RequireAnalyticalAsync(request.CategoryId, ct);
        if (!SystemCategories.IsPatrimonySection(account.Section))
            throw new ValidationException("categoryId", "Patrimônio só aceita contas de Ativo ou Passivo.");

        item.SetCategory(account.Id);
        item.SetName(request.Name);
        item.SetAmount(request.Amount);
        await _uow.SaveChangesAsync(ct);

        var accounts = (await _categories.ListAsync(null, true, false, ct)).ToDictionary(c => c.Id);
        return ToResponse(item, accounts);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var item = await _repo.GetAsync<PatrimonyItem>(id, ct) ?? throw new NotFoundException("Item de patrimônio", id);
        _repo.SoftDelete(item);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ReserveResponse> GetReserveAsync(string? referenceYm, CancellationToken ct, int targetMonths = 6)
    {
        var reference = string.IsNullOrWhiteSpace(referenceYm) ? Competence.From(DateTime.UtcNow) : Competence.Require(referenceYm);
        var essentialIds = (await _categories.ListAsync(CategorySection.Essential, false, true, ct))
            .Select(c => c.Id)
            .ToList();
        var balance = await GetReserveBalanceAsync(ct);
        var target = Math.Clamp(targetMonths, 1, 24);

        if (essentialIds.Count == 0)
            return BuildReserve(reference, 0, 0, "None", balance, target);

        var monthly = new List<decimal>();
        for (var back = 1; back <= 3; back++)
        {
            var ym = Competence.AddMonths(reference, -back);
            var spent = await _repo.SumAsync<Entry>(
                e => e.CompetenceYm == ym
                     && (e.Type == EntryType.Expense || e.Type == EntryType.Contribution)
                     && e.CategoryId != null && essentialIds.Contains(e.CategoryId.Value),
                e => e.Amount, ct);
            spent += (await _repo.ListAsync<CardPurchase>(
                p => p.FirstInvoiceYm.CompareTo(CardPurchase.InvoiceWindowStart(ym)) >= 0
                     && p.FirstInvoiceYm.CompareTo(ym) <= 0
                     && essentialIds.Contains(p.CategoryId),
                ct)).Sum(p => p.AmountInCompetence(ym));

            if (spent > 0)
                monthly.Add(spent);
        }

        if (monthly.Count > 0)
            return BuildReserve(
                reference,
                decimal.Round(monthly.Average(), 2),
                monthly.Count,
                monthly.Count == 3 ? "Last3Months" : "AvailableMonths",
                balance,
                target);

        var budget = await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == reference, ct, track: false);
        if (budget is null)
            return BuildReserve(reference, 0, 0, "None", balance, target);

        var planned = await _repo.SumAsync<BudgetLine>(
            l => l.BudgetId == budget.Id && essentialIds.Contains(l.CategoryId), l => l.PlannedAmount, ct);
        return planned > 0
            ? BuildReserve(reference, planned, 0, "Budget", balance, target)
            : BuildReserve(reference, 0, 0, "None", balance, target);
    }

    private async Task<decimal> GetReserveBalanceAsync(CancellationToken ct)
    {
        var reserveCategory = await _repo.FirstOrDefaultAsync<Category>(
            c => c.Code == "LIFE_RESERVE", ct, track: false);
        if (reserveCategory is null)
            return 0;

        var projects = await _repo.ListAsync<LifeProject>(p => p.CategoryId == reserveCategory.Id, ct);
        if (projects.Count > 0)
            return projects.Sum(p => p.AccumulatedAmount);

        return await _repo.SumAsync<Entry>(
            e => e.CategoryId == reserveCategory.Id && e.Type == EntryType.Contribution,
            e => e.Amount, ct);
    }

    private static ReserveResponse BuildReserve(
        string reference,
        decimal monthlyAverage,
        int monthsConsidered,
        string source,
        decimal balance,
        int targetMonths)
    {
        var covered = monthlyAverage > 0 ? decimal.Round(balance / monthlyAverage, 1) : 0m;
        var progress = targetMonths <= 0
            ? 0m
            : Math.Min(100m, decimal.Round(covered / targetMonths * 100, 1));
        return new ReserveResponse(reference, monthlyAverage, monthsConsidered, source, balance, covered, targetMonths, progress);
    }

    private static PatrimonyItemResponse ToResponse(PatrimonyItem item, Dictionary<Guid, CategoryResponse> accounts)
    {
        var account = accounts.GetValueOrDefault(item.CategoryId);
        var group = account?.ParentId is Guid pid && accounts.TryGetValue(pid, out var parent)
            ? parent.Name
            : account?.Name ?? "—";
        return new(
            item.Id,
            item.CategoryId,
            account?.Name ?? "—",
            item.Name,
            account?.Section ?? CategorySection.Asset,
            group,
            item.Amount);
    }
}
