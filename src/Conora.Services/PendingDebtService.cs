using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class PendingDebtService
{
    private static readonly CategorySection[] ExpenseSections =
    [
        CategorySection.Discount,
        CategorySection.Debt,
        CategorySection.Essential,
        CategorySection.LifeProject,
        CategorySection.Social
    ];

    private readonly IFinanceRepository _repo;
    private readonly BudgetService _budgets;

    public PendingDebtService(IFinanceRepository repo, BudgetService budgets)
    {
        _repo = repo;
        _budgets = budgets;
    }

    public async Task<IReadOnlyList<CloseGapResponse>> ListGapsAsync(string competenceYm, CancellationToken ct)
    {
        var ym = Competence.Require(competenceYm);
        var budget = await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == ym, ct, track: false);
        if (budget is null)
            return [];

        var lines = await _repo.ListAsync<BudgetLine>(l => l.BudgetId == budget.Id, ct);
        var categories = (await _repo.ListAsync<Category>(
                c => !c.IsPendingReview && c.Level == CategoryLevel.Analytical,
                ct))
            .ToDictionary(c => c.Id);
        var actuals = await _budgets.GetActualsAsync(ym, ct);
        var taken = (await _repo.ListAsync<PendingDebt>(d => d.CompetenceYm == ym, ct))
            .Select(d => d.CategoryId)
            .ToHashSet();

        var gaps = new List<CloseGapResponse>();
        foreach (var line in lines)
        {
            if (!categories.TryGetValue(line.CategoryId, out var category))
                continue;
            if (!ExpenseSections.Contains(category.Section))
                continue;

            var actual = actuals.GetValueOrDefault(line.CategoryId);
            var open = decimal.Round(line.PlannedAmount - actual, 2);
            if (open <= 0)
                continue;

            gaps.Add(new CloseGapResponse(
                category.Id,
                category.Name,
                line.PlannedAmount,
                actual,
                open,
                taken.Contains(category.Id)));
        }

        return gaps.OrderBy(g => g.CategoryName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Adds new debts to the current unit of work. Does not save.
    /// An existing row for the same month and category is left untouched.
    /// </summary>
    public async Task StageAsync(string competenceYm, IReadOnlyList<Guid> categoryIds, CancellationToken ct)
    {
        if (categoryIds.Count == 0)
            return;

        var gaps = (await ListGapsAsync(competenceYm, ct)).ToDictionary(g => g.CategoryId);
        foreach (var categoryId in categoryIds.Distinct())
        {
            if (!gaps.TryGetValue(categoryId, out var gap))
                throw new ValidationException("categoryIds", "Só dá para registrar dívida de uma conta que ficou em aberto neste mês.");
            if (gap.AlreadyRegistered)
                continue;

            _repo.Add(PendingDebt.Open(competenceYm, categoryId, gap.OpenAmount));
        }
    }

    public async Task<IReadOnlyList<PendingDebtResponse>> ListOpenAsync(CancellationToken ct)
    {
        var rows = await _repo.ListAsync<PendingDebt>(d => d.PaidAt == null, ct);
        if (rows.Count == 0)
            return [];

        var names = (await _repo.ListAsync<Category>(null, ct)).ToDictionary(c => c.Id, c => c.Name);
        return rows
            .OrderBy(d => d.CompetenceYm, StringComparer.Ordinal)
            .ThenBy(d => names.GetValueOrDefault(d.CategoryId, ""), StringComparer.OrdinalIgnoreCase)
            .Select(d => new PendingDebtResponse(
                d.Id,
                d.CompetenceYm,
                d.CategoryId,
                names.GetValueOrDefault(d.CategoryId, "—"),
                d.OriginalAmount,
                d.OpenAmount,
                d.PaidAt))
            .ToList();
    }

    public async Task ApplyAsync(Guid id, Guid categoryId, decimal amount, CancellationToken ct)
    {
        await ApplyManyAsync([id], categoryId, amount, ct);
    }

    /// <summary>
    /// Walks selected debts in order and allocates the entry amount until it runs out.
    /// Each debt takes min(remaining, open). PaidAt is set only when that debt reaches zero.
    /// </summary>
    public async Task ApplyManyAsync(
        IReadOnlyList<Guid> debtIds,
        Guid categoryId,
        decimal amount,
        CancellationToken ct)
    {
        if (debtIds.Count == 0)
            return;
        if (amount <= 0)
            throw new ValidationException("amount", "Valor deve ser maior que zero.");

        var remaining = decimal.Round(amount, 2);
        var seen = new HashSet<Guid>();
        foreach (var id in debtIds)
        {
            if (remaining <= 0)
                break;
            if (!seen.Add(id))
                throw new ValidationException("pendingDebtIds", "A mesma dívida foi selecionada mais de uma vez.");

            var debt = await _repo.GetAsync<PendingDebt>(id, ct)
                       ?? throw new NotFoundException("Dívida", id);
            if (debt.CategoryId != categoryId)
                throw new ValidationException("pendingDebtIds", "A dívida não é desta categoria.");

            var applied = debt.ApplyPayment(remaining);
            remaining = decimal.Round(remaining - applied, 2);
        }
    }
}
