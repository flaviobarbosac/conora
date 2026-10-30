using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.CategoryAccess;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed partial class BudgetService
{
    private sealed record AccountSum(Guid CategoryId, decimal Amount);

    private static readonly CategorySection[] ExpenseSections =
    [
        CategorySection.Discount,
        CategorySection.Debt,
        CategorySection.Essential,
        CategorySection.LifeProject,
        CategorySection.Social
    ];

    /** Cash-flow sections shown in budget/Raio-X (receita + despesa). */
    private static readonly CategorySection[] BudgetSections =
    [
        CategorySection.Income,
        CategorySection.Discount,
        CategorySection.Debt,
        CategorySection.Essential,
        CategorySection.LifeProject,
        CategorySection.Social
    ];

    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly PlanService _plan;
    private readonly MonthService _months;
    private readonly CategoryService _categories;
    private readonly FamilyGroupService _family;
    private readonly ICategoryAccessPolicyFactory _categoryAccess;

    public BudgetService(
        IFinanceRepository repo,
        IUnitOfWork uow,
        PlanService plan,
        MonthService months,
        CategoryService categories,
        FamilyGroupService family,
        ICategoryAccessPolicyFactory categoryAccess)
    {
        _repo = repo;
        _uow = uow;
        _plan = plan;
        _months = months;
        _categories = categories;
        _family = family;
        _categoryAccess = categoryAccess;
    }

    /// <param name="attentionPercent">Start of the yellow Attention band (1–99). Limit at 100% and Exceeded above stay fixed.</param>
    public static (decimal? Percent, string Status) Evaluate(decimal planned, decimal actual, int attentionPercent = 70)
    {
        if (planned <= 0)
            return (null, actual > 0 ? "Exceeded" : "Ok");

        var threshold = Math.Clamp(attentionPercent, 1, 99) / 100m;
        var ratio = actual / planned;
        var percent = decimal.Round(ratio * 100, 1);
        var status = ratio switch
        {
            > 1m => "Exceeded",
            1m => "Limit",
            var r when r >= threshold => "Attention",
            _ => "Ok"
        };
        return (percent, status);
    }

    

    /// <summary>Purchases whose invoice plan can place a slice in the competence. The caller sums <see cref="CardPurchase.AmountInCompetence"/>.</summary>
    private async Task<IReadOnlyList<CardPurchase>> ListPurchasesTouchingAsync(
        string ym,
        HashSet<Guid> categoryIds,
        IReadOnlyList<Guid> userIds,
        bool multi,
        CancellationToken ct)
    {
        var windowStart = CardPurchase.InvoiceWindowStart(ym);
        return multi
            ? await _repo.ListForUsuariosAsync<CardPurchase>(
                userIds,
                p => p.FirstInvoiceYm.CompareTo(windowStart) >= 0
                     && p.FirstInvoiceYm.CompareTo(ym) <= 0
                     && categoryIds.Contains(p.CategoryId),
                ct)
            : await _repo.ListAsync<CardPurchase>(
                p => p.FirstInvoiceYm.CompareTo(windowStart) >= 0
                     && p.FirstInvoiceYm.CompareTo(ym) <= 0
                     && categoryIds.Contains(p.CategoryId),
                ct);
    }

    

    

    /// <summary>Repeats the current month's planned amount for an account across the next months (including start).</summary>

    /// <summary>Splits a total into monthly planned amounts starting at the competence.</summary>

    private async Task<Dictionary<Guid, decimal>> RemapActualsAsync(
        Dictionary<Guid, decimal> actuals,
        Dictionary<Guid, CategoryResponse> accounts,
        IReadOnlyList<Guid> userIds,
        CancellationToken ct)
    {
        var remapped = new Dictionary<Guid, decimal>();
        foreach (var (accountId, amount) in actuals)
        {
            if (accounts.ContainsKey(accountId))
            {
                remapped[accountId] = remapped.GetValueOrDefault(accountId) + amount;
                continue;
            }

            var remote = await _repo.FirstOrDefaultForUsuariosAsync<Category>(userIds, c => c.Id == accountId, ct);
            var local = remote is null
                ? null
                : accounts.Values.FirstOrDefault(c =>
                    (remote.Code is not null && c.Code == remote.Code) || c.Name == remote.Name);
            var target = local?.Id ?? accountId;
            remapped[target] = remapped.GetValueOrDefault(target) + amount;
        }

        return remapped;
    }

    private static List<BudgetLineResponse> BuildDetailLines(
        IReadOnlyList<(Guid CategoryId, decimal Planned, int? ExpenseDay)> lines,
        Dictionary<Guid, decimal> actuals,
        Dictionary<Guid, CategoryResponse> accounts,
        int attentionPercent = 70)
    {
        var planned = new Dictionary<Guid, decimal>();
        var days = new Dictionary<Guid, int?>();
        foreach (var (categoryId, amount, expenseDay) in lines)
        {
            planned[categoryId] = planned.GetValueOrDefault(categoryId) + amount;
            if (expenseDay is not null)
                days[categoryId] = expenseDay;
        }

        var parents = accounts.Values.ToDictionary(a => a.Id);
        var responses = new List<BudgetLineResponse>();
        foreach (var id in planned.Keys.Concat(actuals.Keys).Distinct())
        {
            var plan = planned.GetValueOrDefault(id);
            var actual = actuals.GetValueOrDefault(id);
            var expenseDay = days.GetValueOrDefault(id);
            if (plan == 0 && actual == 0 && expenseDay is null)
                continue;

            var account = accounts.GetValueOrDefault(id);
            var section = account?.Section ?? CategorySection.Social;
            var group = account?.ParentId is Guid pid && parents.TryGetValue(pid, out var parent)
                ? parent.Name
                : account?.Name ?? "—";
            var name = account?.Name ?? "—";
            var (percent, status) = Evaluate(plan, actual, attentionPercent);
            responses.Add(new BudgetLineResponse(
                id, name, account?.ParentId, group, section, CategoryLevel.Analytical,
                plan, actual, plan - actual, percent, status, IsGroup: false, expenseDay));
        }

        return responses
            .OrderBy(l => l.Section)
            .ThenBy(l => l.GroupName)
            .ThenBy(l => l.CategoryName)
            .ToList();
    }

    /// <summary>
    /// Adds a payable amount to the planned line for the due-date competence (boleto scan).
    /// Does not create an Entry — only BudgetLine planned.
    /// </summary>
    public async Task<(string CompetenceYm, decimal PlannedAmount, int? ExpenseDay)> AddPayableFromBoletoAsync(
        Guid categoryId,
        decimal amount,
        DateTime dueDate,
        CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var ym = Competence.From(dueDate);
        await _months.EnsureOpenAsync(ym, ct);
        var expenseDay = Math.Clamp(dueDate.Day, 1, 31);
        var budget = await GetOrCreateBudgetAsync(ym, BudgetMode.Detailed, ct);
        var previous = await GetPlannedAsync(budget.Id, categoryId, ct);
        var planned = decimal.Round(previous + amount, 2);
        await SetLineAsync(budget, categoryId, planned, ct, expenseDay, updateDay: true);
        await _uow.SaveChangesAsync(ct);
        return (ym, planned, expenseDay);
    }

    private async Task<Budget> GetOrCreateBudgetAsync(string ym, BudgetMode mode, CancellationToken ct)
    {
        var budget = await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == ym, ct);
        if (budget is not null)
            return budget;

        budget = Budget.Create(ym, mode);
        _repo.Add(budget);
        return budget;
    }

    private async Task SetLineAsync(
        Budget budget,
        Guid categoryId,
        decimal planned,
        CancellationToken ct,
        int? expenseDay = null,
        bool updateDay = false)
    {
        var line = await _repo.FirstOrDefaultAsync<BudgetLine>(
            l => l.BudgetId == budget.Id && l.CategoryId == categoryId, ct);
        if (line is null)
            _repo.Add(BudgetLine.Create(budget.Id, categoryId, planned, updateDay ? expenseDay : null));
        else
        {
            line.SetPlanned(planned);
            if (updateDay)
                line.SetExpenseDay(expenseDay);
        }
    }

    private async Task<decimal> GetPlannedAsync(Guid budgetId, Guid categoryId, CancellationToken ct)
    {
        var line = await _repo.FirstOrDefaultAsync<BudgetLine>(
            l => l.BudgetId == budgetId && l.CategoryId == categoryId, ct, track: false);
        return line?.PlannedAmount ?? 0;
    }

    private static decimal Project(string ym, decimal actual)
    {
        var today = DateTime.UtcNow;
        if (ym != Competence.From(today) || today.Day == 0)
            return actual;

        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        return decimal.Round(actual / today.Day * daysInMonth, 2);
    }

    private async Task<decimal> GetReceivedIncomeAsync(
        string ym,
        IReadOnlyList<Guid> userIds,
        bool multi,
        SharingScope scope,
        CancellationToken ct)
    {
        var selfId = _family.RequireUserId();
        var allowedCategoryIds = (await _repo.ListForUsuariosAsync<Category>(
                userIds,
                c => !c.IsPendingReview && c.Level == CategoryLevel.Analytical && c.Section == CategorySection.Income,
                ct))
            .Select(c => c.Id)
            .ToHashSet();

        if (multi)
        {
            return (await _repo.ListForUsuariosAsync<Entry>(
                userIds,
                e => e.CompetenceYm == ym
                     && e.Type == EntryType.Income
                     && (
                         (e.CategoryId != null && allowedCategoryIds.Contains(e.CategoryId.Value))
                         || (e.UsuarioId == selfId && e.CategoryId == null)),
                ct)).Sum(e => e.Amount);
        }

        return await _repo.SumAsync<Entry>(
            e => e.CompetenceYm == ym
                 && e.Type == EntryType.Income
                 && (
                     (e.CategoryId != null && allowedCategoryIds.Contains(e.CategoryId.Value))
                     || e.CategoryId == null),
            e => e.Amount, ct);
    }

    private async Task<Dictionary<string, decimal>> GetReceivedIncomeByMonthAsync(
        IReadOnlyList<string> months,
        IReadOnlyList<Guid> userIds,
        bool multi,
        CancellationToken ct)
    {
        var monthSet = months.ToHashSet(StringComparer.Ordinal);
        var selfId = _family.RequireUserId();
        var allowedCategoryIds = (await _repo.ListForUsuariosAsync<Category>(
                userIds,
                c => !c.IsPendingReview && c.Level == CategoryLevel.Analytical && c.Section == CategorySection.Income,
                ct))
            .Select(c => c.Id)
            .ToHashSet();

        var entries = multi
            ? await _repo.ListForUsuariosAsync<Entry>(
                userIds,
                e => monthSet.Contains(e.CompetenceYm)
                     && e.Type == EntryType.Income
                     && (
                         (e.CategoryId != null && allowedCategoryIds.Contains(e.CategoryId.Value))
                         || (e.UsuarioId == selfId && e.CategoryId == null)),
                ct)
            : await _repo.ListAsync<Entry>(
                e => monthSet.Contains(e.CompetenceYm)
                     && e.Type == EntryType.Income
                     && (
                         (e.CategoryId != null && allowedCategoryIds.Contains(e.CategoryId.Value))
                         || e.CategoryId == null),
                ct);

        var totals = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var entry in entries)
            totals[entry.CompetenceYm] = totals.GetValueOrDefault(entry.CompetenceYm) + entry.Amount;

        return totals;
    }

    private Task<bool> HasLifeProjectAsync(Guid categoryId, CancellationToken ct)
        => _repo.AnyAsync<LifeProject>(p => p.CategoryId == categoryId, ct);

    private async Task EnsureManualPlanAllowedAsync(Guid categoryId, Category account, CancellationToken ct)
    {
        if (account.Section != CategorySection.LifeProject)
            return;
        if (await HasLifeProjectAsync(categoryId, ct))
            throw new ValidationException(
                "categoryId",
                "O previsto desta conta vem do projeto de vida. Crie o projeto pela conta ou apague o projeto para liberar o valor.");
    }

    private static CategoryResponse ToResponse(Category c)
        => new(
            c.Id, c.ParentId, c.Name, c.Code, c.DisplayNumber, c.Level, c.Section, c.IsSystem, c.IsActive,
            c.SortOrder, c.AcceptsPosting, c.SharingScope, c.CreatedByUsuarioId, null, c.IsPendingReview);
}
