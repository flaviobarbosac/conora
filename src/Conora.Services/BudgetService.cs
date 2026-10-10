using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class BudgetService
{
    private sealed record AccountSum(Guid CategoryId, decimal Amount);

    private static readonly CategorySection[] ExpenseSections =
    [
        CategorySection.LifeProject,
        CategorySection.Essential,
        CategorySection.Social,
        CategorySection.Discount
    ];

    /** Cash-flow sections shown in budget/Raio-X (receita + despesa). */
    private static readonly CategorySection[] BudgetSections =
    [
        CategorySection.Income,
        CategorySection.Discount,
        CategorySection.LifeProject,
        CategorySection.Essential,
        CategorySection.Social
    ];

    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly PlanService _plan;
    private readonly MonthService _months;
    private readonly CategoryService _categories;
    private readonly FamilyGroupService _family;

    public BudgetService(
        IFinanceRepository repo,
        IUnitOfWork uow,
        PlanService plan,
        MonthService months,
        CategoryService categories,
        FamilyGroupService family)
    {
        _repo = repo;
        _uow = uow;
        _plan = plan;
        _months = months;
        _categories = categories;
        _family = family;
    }

    public static (decimal? Percent, string Status) Evaluate(decimal planned, decimal actual)
    {
        if (planned <= 0)
            return (null, actual > 0 ? "Exceeded" : "Ok");

        var ratio = actual / planned;
        var percent = decimal.Round(ratio * 100, 1);
        var status = ratio switch
        {
            > 1m => "Exceeded",
            1m => "Limit",
            >= 0.7m => "Attention",
            _ => "Ok"
        };
        return (percent, status);
    }

    public async Task<Dictionary<Guid, decimal>> GetActualsAsync(string competenceYm, CancellationToken ct)
    {
        var ym = Competence.Require(competenceYm);
        var userIds = await _family.GetReadableUsuarioIdsAsync(ct);
        var multi = userIds.Count > 1;
        var until = ActualUntilUtc(ym);

        var entries = multi
            ? await _repo.QueryAnyTenantAsync<Entry, AccountSum>(q => q
                .Where(e => userIds.Contains(e.UsuarioId)
                            && e.CompetenceYm == ym
                            && e.OccurredAt <= until
                            && (e.Type == EntryType.Income
                                || e.Type == EntryType.Expense
                                || e.Type == EntryType.Contribution
                                || e.Type == EntryType.ProjectContribution)
                            && e.CategoryId != null)
                .GroupBy(e => e.CategoryId!.Value)
                .Select(g => new AccountSum(g.Key, g.Sum(x => x.Amount))), ct)
            : await _repo.QueryAsync<Entry, AccountSum>(q => q
                .Where(e => e.CompetenceYm == ym
                            && e.OccurredAt <= until
                            && (e.Type == EntryType.Income
                                || e.Type == EntryType.Expense
                                || e.Type == EntryType.Contribution
                                || e.Type == EntryType.ProjectContribution)
                            && e.CategoryId != null)
                .GroupBy(e => e.CategoryId!.Value)
                .Select(g => new AccountSum(g.Key, g.Sum(x => x.Amount))), ct);

        var purchases = multi
            ? await _repo.QueryAnyTenantAsync<CardPurchase, AccountSum>(q => q
                .Where(p => userIds.Contains(p.UsuarioId) && p.CompetenceYm == ym && p.PurchasedAt <= until)
                .GroupBy(p => p.CategoryId)
                .Select(g => new AccountSum(g.Key, g.Sum(x => x.Amount))), ct)
            : await _repo.QueryAsync<CardPurchase, AccountSum>(q => q
                .Where(p => p.CompetenceYm == ym && p.PurchasedAt <= until)
                .GroupBy(p => p.CategoryId)
                .Select(g => new AccountSum(g.Key, g.Sum(x => x.Amount))), ct);

        var totals = new Dictionary<Guid, decimal>();
        foreach (var row in entries.Concat(purchases))
            totals[row.CategoryId] = totals.GetValueOrDefault(row.CategoryId) + row.Amount;

        return totals;
    }

    public async Task<BudgetResponse> GetAsync(string competenceYm, CancellationToken ct)
    {
        var ym = Competence.Require(competenceYm);
        var userIds = await _family.GetReadableUsuarioIdsAsync(ct);
        var multi = userIds.Count > 1;

        var budgets = multi
            ? await _repo.ListAnyTenantAsync<Budget>(b => userIds.Contains(b.UsuarioId) && b.CompetenceYm == ym, ct)
            : (await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == ym, ct, track: false) is { } one ? [one] : []);
        var budgetIds = budgets.Select(b => b.Id).ToList();
        var lines = budgetIds.Count == 0
            ? []
            : multi
                ? await _repo.ListAnyTenantAsync<BudgetLine>(l => budgetIds.Contains(l.BudgetId), ct)
                : await _repo.ListAsync<BudgetLine>(l => budgetIds.Contains(l.BudgetId), ct);

        var accounts = (await _categories.ListAsync(null, true, false, ct)).ToDictionary(c => c.Id);
        var plannedByCode = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var accountIdByCode = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            CategoryResponse? account = accounts.GetValueOrDefault(line.CategoryId);
            if (account is null && multi)
            {
                var remote = await _repo.FirstOrDefaultAnyTenantAsync<Category>(c => c.Id == line.CategoryId, ct);
                if (remote is not null)
                    account = ToResponse(remote);
            }

            var key = account?.Code ?? account?.Name ?? line.CategoryId.ToString();
            plannedByCode[key] = plannedByCode.GetValueOrDefault(key) + line.PlannedAmount;
            if (account is not null)
            {
                var local = accounts.Values.FirstOrDefault(c =>
                    (account.Code is not null && c.Code == account.Code) || c.Name == account.Name);
                if (local is not null)
                    accountIdByCode[key] = local.Id;
            }
        }

        var actuals = await GetActualsAsync(ym, ct);
        if (multi)
            actuals = await RemapActualsAsync(actuals, accounts, ct);

        var sources = multi
            ? (await _repo.ListAnyTenantAsync<IncomeSource>(s => userIds.Contains(s.UsuarioId) && s.CompetenceYm == ym, ct))
                .OrderBy(s => s.Name)
                .Select(s => new BudgetIncomeSourceResponse(s.Id, s.Name, s.NetSpendable))
                .ToList()
            : (await _repo.ListAsync<IncomeSource>(s => s.CompetenceYm == ym, ct))
                .OrderBy(s => s.Name)
                .Select(s => new BudgetIncomeSourceResponse(s.Id, s.Name, s.NetSpendable))
                .ToList();
        var spendable = sources.Sum(s => s.NetSpendable);

        var plannedPairs = plannedByCode.Select(kv =>
        {
            var id = accountIdByCode.GetValueOrDefault(kv.Key);
            return (CategoryId: id == Guid.Empty ? Guid.NewGuid() : id, Planned: kv.Value);
        }).ToList();

        foreach (var pair in plannedPairs.Where(p => !accounts.ContainsKey(p.CategoryId)))
        {
            accounts[pair.CategoryId] = new CategoryResponse(
                pair.CategoryId, null, "—", null, null, CategoryLevel.Analytical, CategorySection.Social,
                false, true, 0, true);
        }

        var detailLines = BuildDetailLines(plannedPairs, actuals, accounts);
        var mode = BudgetMode.Detailed;
        var displayLines = detailLines;

        var sections = BudgetSections.Select(section =>
        {
            var sectionLines = displayLines.Where(l => l.Section == section).ToList();
            var planned = sectionLines.Sum(l => l.PlannedAmount);
            var actual = sectionLines.Sum(l => l.ActualAmount);
            decimal? pct = spendable > 0 ? decimal.Round(actual / spendable * 100, 1) : null;
            return new BudgetSectionResponse(
                section, SystemCategories.SectionLabel(section), planned, actual, pct, sectionLines);
        }).ToList();

        var totalPlanned = displayLines.Sum(l => l.PlannedAmount);
        var totalActual = actuals.Values.Sum();
        var receivedIncome = await GetReceivedIncomeAsync(ym, userIds, multi, ct);
        return new BudgetResponse(
            ym,
            mode,
            totalPlanned,
            totalActual,
            Project(ym, totalActual),
            spendable,
            receivedIncome,
            spendable - totalActual,
            sources,
            sections,
            displayLines);
    }

    public async Task<BudgetYearResponse> GetYearAsync(int year, CancellationToken ct)
    {
        if (year is < 2000 or > 2100)
            throw new ValidationException("year", "Ano inválido.");

        var months = Enumerable.Range(1, 12).Select(m => $"{year}-{m:D2}").ToList();
        var accounts = (await _categories.ListAsync(null, true, true, ct)).ToDictionary(c => c.Id);
        var plannedByMonth = new Dictionary<string, Dictionary<Guid, decimal>>();
        var actualByMonth = new Dictionary<string, Dictionary<Guid, decimal>>();

        var userIds = await _family.GetReadableUsuarioIdsAsync(ct);
        var multi = userIds.Count > 1;
        foreach (var ym in months)
        {
            var budgets = multi
                ? await _repo.ListAnyTenantAsync<Budget>(b => userIds.Contains(b.UsuarioId) && b.CompetenceYm == ym, ct)
                : (await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == ym, ct, track: false) is { } one ? [one] : []);
            var budgetIds = budgets.Select(b => b.Id).ToList();
            var lines = budgetIds.Count == 0
                ? []
                : multi
                    ? await _repo.ListAnyTenantAsync<BudgetLine>(l => budgetIds.Contains(l.BudgetId), ct)
                    : await _repo.ListAsync<BudgetLine>(l => budgetIds.Contains(l.BudgetId), ct);

            var planned = new Dictionary<Guid, decimal>();
            foreach (var line in lines)
            {
                var localId = line.CategoryId;
                if (multi && !accounts.ContainsKey(localId))
                {
                    var remote = await _repo.FirstOrDefaultAnyTenantAsync<Category>(c => c.Id == line.CategoryId, ct);
                    var match = remote is null
                        ? null
                        : accounts.Values.FirstOrDefault(c =>
                            (remote.Code is not null && c.Code == remote.Code) || c.Name == remote.Name);
                    if (match is not null)
                        localId = match.Id;
                }

                planned[localId] = planned.GetValueOrDefault(localId) + line.PlannedAmount;
            }

            plannedByMonth[ym] = planned;
            actualByMonth[ym] = await GetActualsAsync(ym, ct);
        }

        var accountIds = plannedByMonth.Values.SelectMany(d => d.Keys)
            .Concat(actualByMonth.Values.SelectMany(d => d.Keys))
            .Distinct()
            .ToList();

        var parents = accounts.Values.ToDictionary(a => a.Id);
        var yearLines = accountIds
            .Select(id =>
            {
                var account = accounts.GetValueOrDefault(id);
                var section = account?.Section ?? CategorySection.Social;
                var group = account?.ParentId is Guid pid && parents.TryGetValue(pid, out var parent)
                    ? parent.Name
                    : account?.Name ?? "—";
                var name = account?.Name ?? "—";
                var cells = months.Select(ym => new BudgetYearMonthCell(
                    ym,
                    plannedByMonth[ym].GetValueOrDefault(id),
                    actualByMonth[ym].GetValueOrDefault(id))).ToList();
                return new BudgetYearLineResponse(id, name, group, section, cells);
            })
            .OrderBy(l => l.Section)
            .ThenBy(l => l.GroupName)
            .ThenBy(l => l.CategoryName)
            .ToList();

        var totals = months.Select(ym => new BudgetYearMonthCell(
            ym,
            plannedByMonth[ym].Values.Sum(),
            actualByMonth[ym].Values.Sum())).ToList();

        return new BudgetYearResponse(year, months, yearLines, totals);
    }

    public async Task<BudgetResponse> UpsertAsync(string competenceYm, UpsertBudgetRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var ym = Competence.Require(competenceYm);
        await _months.EnsureOpenAsync(ym, ct);

        var budget = await GetOrCreateBudgetAsync(ym, BudgetMode.Detailed, ct);
        budget.SetMode(BudgetMode.Detailed);

        foreach (var input in request.Lines.GroupBy(l => l.CategoryId).Select(g => g.Last()))
        {
            var account = await _categories.RequireAnalyticalAsync(input.CategoryId, ct);
            if (!BudgetSections.Contains(account.Section))
                throw new ValidationException("lines", $"A conta '{account.Name}' não aceita orçamento (somente receita e despesa).");

            await SetLineAsync(budget, input.CategoryId, decimal.Round(input.PlannedAmount, 2), ct);
        }

        await _uow.SaveChangesAsync(ct);
        return await GetAsync(ym, ct);
    }

    public async Task<BudgetResponse> DeleteLineAsync(string competenceYm, Guid categoryId, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var ym = Competence.Require(competenceYm);
        await _months.EnsureOpenAsync(ym, ct);

        var budget = await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == ym, ct)
                     ?? throw new NotFoundException($"Orçamento de {ym} não encontrado.");
        var line = await _repo.FirstOrDefaultAsync<BudgetLine>(
                       l => l.BudgetId == budget.Id && l.CategoryId == categoryId, ct)
                   ?? throw new NotFoundException("Linha de orçamento", categoryId);

        _repo.SoftDelete(line);
        await _uow.SaveChangesAsync(ct);
        return await GetAsync(ym, ct);
    }

    public async Task<BudgetResponse> CopyFromPreviousAsync(
        string competenceYm,
        CopyPreviousBudgetRequest? request,
        CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var ym = Competence.Require(competenceYm);
        await _months.EnsureOpenAsync(ym, ct);
        var overwrite = request?.Overwrite ?? false;

        var previousYm = Competence.AddMonths(ym, -1);
        var previous = await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == previousYm, ct, track: false)
                       ?? throw new NotFoundException($"Não há orçamento em {previousYm} para copiar.");
        var previousLines = await _repo.ListAsync<BudgetLine>(l => l.BudgetId == previous.Id, ct);

        var budget = await GetOrCreateBudgetAsync(ym, BudgetMode.Detailed, ct);
        budget.SetMode(BudgetMode.Detailed);
        foreach (var line in previousLines)
        {
            if (!overwrite && await GetPlannedAsync(budget.Id, line.CategoryId, ct) > 0)
                continue;
            await SetLineAsync(budget, line.CategoryId, line.PlannedAmount, ct);
        }

        await _uow.SaveChangesAsync(ct);
        return await GetAsync(ym, ct);
    }

    /// <summary>Repeats the current month's planned amount for an account across the next months (including start).</summary>
    public async Task<BudgetResponse> RepeatAsync(string competenceYm, RepeatBudgetRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var startYm = Competence.Require(competenceYm);
        if (request.MonthCount is < 2 or > 120)
            throw new ValidationException("monthCount", "Informe entre 2 e 120 meses.");

        var account = await _categories.RequireAnalyticalAsync(request.CategoryId, ct);
        if (!BudgetSections.Contains(account.Section))
            throw new ValidationException("categoryId", $"A conta '{account.Name}' não aceita orçamento.");

        await _months.EnsureOpenAsync(startYm, ct);
        var startBudget = await GetOrCreateBudgetAsync(startYm, BudgetMode.Detailed, ct);
        var amount = request.PlannedAmount is decimal explicitAmount
            ? decimal.Round(explicitAmount, 2)
            : await GetPlannedAsync(startBudget.Id, request.CategoryId, ct);
        if (amount <= 0)
            throw new ValidationException("categoryId", "Informe um previsto neste mês antes de repetir.");

        if (request.PlannedAmount is not null)
            await SetLineAsync(startBudget, request.CategoryId, amount, ct);

        for (var i = 1; i < request.MonthCount; i++)
        {
            var ym = Competence.AddMonths(startYm, i);
            await _months.EnsureOpenAsync(ym, ct);
            var budget = await GetOrCreateBudgetAsync(ym, BudgetMode.Detailed, ct);
            if (!request.Overwrite && await GetPlannedAsync(budget.Id, request.CategoryId, ct) > 0)
                continue;
            await SetLineAsync(budget, request.CategoryId, amount, ct);
        }

        await _uow.SaveChangesAsync(ct);
        return await GetAsync(startYm, ct);
    }

    /// <summary>Splits a total into monthly planned amounts starting at the competence.</summary>
    public async Task<BudgetResponse> InstallmentAsync(
        string competenceYm,
        InstallmentBudgetRequest request,
        CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var startYm = Competence.Require(competenceYm);
        if (request.InstallmentCount is < 2 or > 120)
            throw new ValidationException("installmentCount", "Informe entre 2 e 120 parcelas.");
        if (request.TotalAmount <= 0)
            throw new ValidationException("totalAmount", "Informe um valor total maior que zero.");

        var account = await _categories.RequireAnalyticalAsync(request.CategoryId, ct);
        if (!BudgetSections.Contains(account.Section))
            throw new ValidationException("categoryId", $"A conta '{account.Name}' não aceita orçamento.");

        var total = decimal.Round(request.TotalAmount, 2);
        var each = decimal.Round(total / request.InstallmentCount, 2);
        var allocated = 0m;

        for (var i = 0; i < request.InstallmentCount; i++)
        {
            var ym = Competence.AddMonths(startYm, i);
            await _months.EnsureOpenAsync(ym, ct);
            var value = i == request.InstallmentCount - 1 ? total - allocated : each;
            allocated += value;

            var budget = await GetOrCreateBudgetAsync(ym, BudgetMode.Detailed, ct);
            if (!request.Overwrite && await GetPlannedAsync(budget.Id, request.CategoryId, ct) > 0)
                continue;
            await SetLineAsync(budget, request.CategoryId, value, ct);
        }

        await _uow.SaveChangesAsync(ct);
        return await GetAsync(startYm, ct);
    }

    public async Task SyncPlannedTitheAsync(string competenceYm, decimal totalTithe, CancellationToken ct)
    {
        var ym = Competence.Require(competenceYm);
        var tithe = await _categories.GetByCodeAsync(SystemCategories.Tithe, ct);
        var budget = await GetOrCreateBudgetAsync(ym, BudgetMode.Simple, ct);
        await SetLineAsync(budget, tithe.Id, Math.Max(0, decimal.Round(totalTithe, 2)), ct);
    }

    private async Task<Dictionary<Guid, decimal>> RemapActualsAsync(
        Dictionary<Guid, decimal> actuals,
        Dictionary<Guid, CategoryResponse> accounts,
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

            var remote = await _repo.FirstOrDefaultAnyTenantAsync<Category>(c => c.Id == accountId, ct);
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
        IReadOnlyList<(Guid CategoryId, decimal Planned)> lines,
        Dictionary<Guid, decimal> actuals,
        Dictionary<Guid, CategoryResponse> accounts)
    {
        var planned = new Dictionary<Guid, decimal>();
        foreach (var (categoryId, amount) in lines)
            planned[categoryId] = planned.GetValueOrDefault(categoryId) + amount;

        var parents = accounts.Values.ToDictionary(a => a.Id);
        var responses = new List<BudgetLineResponse>();
        foreach (var id in planned.Keys.Concat(actuals.Keys).Distinct())
        {
            var plan = planned.GetValueOrDefault(id);
            var actual = actuals.GetValueOrDefault(id);
            if (plan == 0 && actual == 0)
                continue;

            var account = accounts.GetValueOrDefault(id);
            var section = account?.Section ?? CategorySection.Social;
            var group = account?.ParentId is Guid pid && parents.TryGetValue(pid, out var parent)
                ? parent.Name
                : account?.Name ?? "—";
            var name = account?.Name ?? "—";
            var (percent, status) = Evaluate(plan, actual);
            responses.Add(new BudgetLineResponse(
                id, name, account?.ParentId, group, section, CategoryLevel.Analytical,
                plan, actual, plan - actual, percent, status, IsGroup: false));
        }

        return responses
            .OrderBy(l => l.Section)
            .ThenBy(l => l.GroupName)
            .ThenBy(l => l.CategoryName)
            .ToList();
    }

    public async Task UpsertPlannedForAccountAsync(
        Guid categoryId,
        IReadOnlyList<string> months,
        decimal planned,
        CancellationToken ct)
    {
        foreach (var ym in months)
        {
            var budget = await GetOrCreateBudgetAsync(ym, BudgetMode.Detailed, ct);
            await SetLineAsync(budget, categoryId, planned, ct);
        }

        await _uow.SaveChangesAsync(ct);
    }

    public async Task ClearPlannedForAccountAsync(Guid categoryId, IReadOnlyList<string> months, CancellationToken ct)
    {
        if (months.Count == 0)
            return;

        foreach (var ym in months)
        {
            var budget = await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == ym, ct);
            if (budget is null)
                continue;

            var line = await _repo.FirstOrDefaultAsync<BudgetLine>(
                l => l.BudgetId == budget.Id && l.CategoryId == categoryId, ct);
            if (line is not null)
                _repo.SoftDelete(line);
        }

        await _uow.SaveChangesAsync(ct);
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

    private async Task SetLineAsync(Budget budget, Guid categoryId, decimal planned, CancellationToken ct)
    {
        var line = await _repo.FirstOrDefaultAsync<BudgetLine>(
            l => l.BudgetId == budget.Id && l.CategoryId == categoryId, ct);
        if (line is null)
            _repo.Add(BudgetLine.Create(budget.Id, categoryId, planned));
        else
            line.SetPlanned(planned);
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

    /// <summary>For the current competence, only counts amounts through today; past months use the full month.</summary>
    private static DateTime ActualUntilUtc(string ym)
    {
        var today = DateTime.UtcNow;
        if (ym != Competence.From(today))
            return DateTime.SpecifyKind(DateTime.MaxValue.AddDays(-1), DateTimeKind.Utc);

        return today;
    }

    private async Task<decimal> GetReceivedIncomeAsync(
        string ym,
        IReadOnlyList<Guid> userIds,
        bool multi,
        CancellationToken ct)
    {
        var until = ActualUntilUtc(ym);
        if (multi)
        {
            return (await _repo.ListAnyTenantAsync<Entry>(
                e => userIds.Contains(e.UsuarioId)
                     && e.CompetenceYm == ym
                     && e.Type == EntryType.Income
                     && e.OccurredAt <= until, ct)).Sum(e => e.Amount);
        }

        return await _repo.SumAsync<Entry>(
            e => e.CompetenceYm == ym && e.Type == EntryType.Income && e.OccurredAt <= until,
            e => e.Amount, ct);
    }

    private static CategoryResponse ToResponse(Category c)
        => new(c.Id, c.ParentId, c.Name, c.Code, c.DisplayNumber, c.Level, c.Section, c.IsSystem, c.IsActive, c.SortOrder, c.AcceptsPosting);
}
