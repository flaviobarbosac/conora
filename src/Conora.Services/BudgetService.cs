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
    private sealed record CategorySum(Guid CategoryId, decimal Amount);

    private static readonly (BudgetBlock Block, string Name)[] BlockOrder =
    [
        (BudgetBlock.Investment, "Investimentos"),
        (BudgetBlock.Essential, "Despesas essenciais"),
        (BudgetBlock.Social, "Despesas sociais")
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

    /// <summary>
    /// Fixed alert rule (spec v1.1): below 70% Ok, 70-99% Attention, exactly 100% Limit, above 100% Exceeded.
    /// A zero plan never divides: percent is null and any spending counts as Exceeded.
    /// </summary>
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

    /// <summary>Realized spending per category: expense/contribution entries plus card purchases (purchase month).</summary>
    public async Task<Dictionary<Guid, decimal>> GetActualsAsync(string competenceYm, CancellationToken ct)
    {
        var ym = Competence.Require(competenceYm);
        var userIds = await _family.GetReadableUsuarioIdsAsync(ct);
        var multi = userIds.Count > 1;

        var entries = multi
            ? await _repo.QueryAnyTenantAsync<Entry, CategorySum>(q => q
                .Where(e => userIds.Contains(e.UsuarioId)
                            && e.CompetenceYm == ym
                            && (e.Type == EntryType.Expense || e.Type == EntryType.Contribution)
                            && e.CategoryId != null)
                .GroupBy(e => e.CategoryId!.Value)
                .Select(g => new CategorySum(g.Key, g.Sum(x => x.Amount))), ct)
            : await _repo.QueryAsync<Entry, CategorySum>(q => q
                .Where(e => e.CompetenceYm == ym
                            && (e.Type == EntryType.Expense || e.Type == EntryType.Contribution)
                            && e.CategoryId != null)
                .GroupBy(e => e.CategoryId!.Value)
                .Select(g => new CategorySum(g.Key, g.Sum(x => x.Amount))), ct);

        var purchases = multi
            ? await _repo.QueryAnyTenantAsync<CardPurchase, CategorySum>(q => q
                .Where(p => userIds.Contains(p.UsuarioId) && p.CompetenceYm == ym)
                .GroupBy(p => p.CategoryId)
                .Select(g => new CategorySum(g.Key, g.Sum(x => x.Amount))), ct)
            : await _repo.QueryAsync<CardPurchase, CategorySum>(q => q
                .Where(p => p.CompetenceYm == ym)
                .GroupBy(p => p.CategoryId)
                .Select(g => new CategorySum(g.Key, g.Sum(x => x.Amount))), ct);

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

        // Merge planned amounts by category code/name across members using category id of the current tenant when possible.
        var plannedByCategoryName = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var categoryIdByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var categories = (await _categories.ListAsync(CategoryKind.Expense, true, ct)).ToDictionary(c => c.Id);
        foreach (var line in lines)
        {
            CategoryResponse? cat = categories.GetValueOrDefault(line.CategoryId);
            if (cat is null && multi)
            {
                var remote = await _repo.FirstOrDefaultAnyTenantAsync<Category>(c => c.Id == line.CategoryId, ct);
                if (remote is not null)
                    cat = new CategoryResponse(remote.Id, remote.Name, remote.Code, remote.Kind, remote.IsSystem, remote.IsActive, remote.IsEssential, remote.BudgetBlock, remote.GroupName);
            }

            var key = cat?.Code ?? cat?.Name ?? line.CategoryId.ToString();
            plannedByCategoryName[key] = plannedByCategoryName.GetValueOrDefault(key) + line.PlannedAmount;
            if (cat is not null && categories.ContainsKey(cat.Id))
                categoryIdByName[key] = cat.Id;
            else if (cat is not null)
            {
                var local = categories.Values.FirstOrDefault(c =>
                    (cat.Code is not null && c.Code == cat.Code) || c.Name == cat.Name);
                if (local is not null)
                    categoryIdByName[key] = local.Id;
            }
        }

        var actuals = await GetActualsAsync(ym, ct);
        // Remap actuals keyed by remote category ids onto local category ids when possible.
        if (multi)
        {
            var remapped = new Dictionary<Guid, decimal>();
            foreach (var (categoryId, amount) in actuals)
            {
                if (categories.ContainsKey(categoryId))
                {
                    remapped[categoryId] = remapped.GetValueOrDefault(categoryId) + amount;
                    continue;
                }

                var remote = await _repo.FirstOrDefaultAnyTenantAsync<Category>(c => c.Id == categoryId, ct);
                var local = remote is null
                    ? null
                    : categories.Values.FirstOrDefault(c =>
                        (remote.Code is not null && c.Code == remote.Code) || c.Name == remote.Name);
                var target = local?.Id ?? categoryId;
                remapped[target] = remapped.GetValueOrDefault(target) + amount;
                if (local is not null && !categories.ContainsKey(categoryId))
                    categories[local.Id] = local;
            }

            actuals = remapped;
        }

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

        var plannedPairs = plannedByCategoryName.Select(kv =>
        {
            var id = categoryIdByName.GetValueOrDefault(kv.Key);
            return (CategoryId: id == Guid.Empty ? Guid.NewGuid() : id, Planned: kv.Value);
        }).ToList();

        // Attach synthetic category metadata for keys without a local id (should be rare).
        foreach (var pair in plannedPairs.Where(p => !categories.ContainsKey(p.CategoryId)))
        {
            categories[pair.CategoryId] = new CategoryResponse(
                pair.CategoryId, "—", null, CategoryKind.Expense, false, true, false, BudgetBlock.Social, "—");
        }

        var detailLines = BuildDetailLines(plannedPairs, actuals, categories);
        var mode = budgets.FirstOrDefault()?.Mode ?? BudgetMode.Simple;
        var displayLines = mode == BudgetMode.Simple
            ? AggregateByGroup(detailLines)
            : detailLines;

        var blocks = BlockOrder.Select(b =>
        {
            var blockLines = displayLines.Where(l => l.Block == b.Block).ToList();
            var planned = blockLines.Sum(l => l.PlannedAmount);
            var actual = blockLines.Sum(l => l.ActualAmount);
            decimal? pct = spendable > 0 ? decimal.Round(actual / spendable * 100, 1) : null;
            return new BudgetBlockResponse(b.Block, b.Name, planned, actual, pct, blockLines);
        }).ToList();

        var totalPlanned = displayLines.Sum(l => l.PlannedAmount);
        var totalActual = actuals.Values.Sum();
        return new BudgetResponse(
            ym,
            mode,
            totalPlanned,
            totalActual,
            Project(ym, totalActual),
            spendable,
            spendable - totalActual,
            sources,
            blocks,
            displayLines);
    }

    public async Task<BudgetYearResponse> GetYearAsync(int year, CancellationToken ct)
    {
        if (year is < 2000 or > 2100)
            throw new ValidationException("year", "Ano inválido.");

        var months = Enumerable.Range(1, 12).Select(m => $"{year}-{m:D2}").ToList();
        var categories = (await _categories.ListAsync(CategoryKind.Expense, true, ct)).ToDictionary(c => c.Id);
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
                if (multi && !categories.ContainsKey(localId))
                {
                    var remote = await _repo.FirstOrDefaultAnyTenantAsync<Category>(c => c.Id == line.CategoryId, ct);
                    var match = remote is null
                        ? null
                        : categories.Values.FirstOrDefault(c =>
                            (remote.Code is not null && c.Code == remote.Code) || c.Name == remote.Name);
                    if (match is not null)
                        localId = match.Id;
                }

                planned[localId] = planned.GetValueOrDefault(localId) + line.PlannedAmount;
            }

            plannedByMonth[ym] = planned;
            actualByMonth[ym] = await GetActualsAsync(ym, ct);
        }

        var categoryIds = plannedByMonth.Values.SelectMany(d => d.Keys)
            .Concat(actualByMonth.Values.SelectMany(d => d.Keys))
            .Distinct()
            .ToList();

        var yearLines = categoryIds
            .Select(id =>
            {
                var cat = categories.GetValueOrDefault(id);
                var block = cat?.BudgetBlock ?? BudgetBlock.Social;
                var group = cat?.GroupName ?? cat?.Name ?? "—";
                var name = cat?.Name ?? "—";
                var cells = months.Select(ym => new BudgetYearMonthCell(
                    ym,
                    plannedByMonth[ym].GetValueOrDefault(id),
                    actualByMonth[ym].GetValueOrDefault(id))).ToList();
                return new BudgetYearLineResponse(id, name, group, block, cells);
            })
            .OrderBy(l => l.Block)
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

        var budget = await GetOrCreateBudgetAsync(ym, request.Mode, ct);
        budget.SetMode(request.Mode);

        foreach (var input in request.Lines.GroupBy(l => l.CategoryId).Select(g => g.Last()))
        {
            var category = await _repo.FirstOrDefaultAsync<Category>(c => c.Id == input.CategoryId, ct, track: false)
                           ?? throw new NotFoundException("Categoria", input.CategoryId);
            if (category.Kind != CategoryKind.Expense || !category.IsActive)
                throw new ValidationException("lines", $"A categoria '{category.Name}' não aceita orçamento de despesa.");

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
        var line = await _repo.FirstOrDefaultAsync<BudgetLine>(l => l.BudgetId == budget.Id && l.CategoryId == categoryId, ct)
                   ?? throw new NotFoundException("Linha de orçamento", categoryId);

        _repo.SoftDelete(line);
        await _uow.SaveChangesAsync(ct);
        return await GetAsync(ym, ct);
    }

    /// <summary>Copies mode and planned amounts from the previous month's budget.</summary>
    public async Task<BudgetResponse> CopyFromPreviousAsync(string competenceYm, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var ym = Competence.Require(competenceYm);
        await _months.EnsureOpenAsync(ym, ct);

        var previousYm = Competence.AddMonths(ym, -1);
        var previous = await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == previousYm, ct, track: false)
                       ?? throw new NotFoundException($"Não há orçamento em {previousYm} para copiar.");
        var previousLines = await _repo.ListAsync<BudgetLine>(l => l.BudgetId == previous.Id, ct);

        var budget = await GetOrCreateBudgetAsync(ym, previous.Mode, ct);
        budget.SetMode(previous.Mode);
        foreach (var line in previousLines)
            await SetLineAsync(budget, line.CategoryId, line.PlannedAmount, ct);

        await _uow.SaveChangesAsync(ct);
        return await GetAsync(ym, ct);
    }

    /// <summary>
    /// Keeps the planned amount of Contribuições/Doações in sync with the diagnosis tithe.
    /// Does not save: the caller owns the unit of work.
    /// </summary>
    public async Task SyncPlannedTitheAsync(string competenceYm, decimal totalTithe, CancellationToken ct)
    {
        var ym = Competence.Require(competenceYm);
        var contributions = await _categories.GetByCodeAsync(SystemCategories.Contributions, ct);
        var budget = await GetOrCreateBudgetAsync(ym, BudgetMode.Simple, ct);
        await SetLineAsync(budget, contributions.Id, Math.Max(0, decimal.Round(totalTithe, 2)), ct);
    }

    private static List<BudgetLineResponse> BuildDetailLines(
        IReadOnlyList<(Guid CategoryId, decimal Planned)> lines,
        Dictionary<Guid, decimal> actuals,
        Dictionary<Guid, CategoryResponse> categories)
    {
        var planned = new Dictionary<Guid, decimal>();
        foreach (var (categoryId, amount) in lines)
            planned[categoryId] = planned.GetValueOrDefault(categoryId) + amount;

        var categoryIds = planned.Keys.Concat(actuals.Keys).Distinct();
        var responses = new List<BudgetLineResponse>();
        foreach (var id in categoryIds)
        {
            var plan = planned.GetValueOrDefault(id);
            var actual = actuals.GetValueOrDefault(id);
            if (plan == 0 && actual == 0)
                continue;

            var cat = categories.GetValueOrDefault(id);
            var block = cat?.BudgetBlock ?? BudgetBlock.Social;
            var group = cat?.GroupName ?? cat?.Name ?? "—";
            var name = cat?.Name ?? "—";
            var (percent, status) = Evaluate(plan, actual);
            responses.Add(new BudgetLineResponse(
                id, name, group, block, plan, actual, plan - actual, percent, status, IsGroup: false));
        }

        return responses
            .OrderBy(l => l.Block)
            .ThenBy(l => l.GroupName)
            .ThenBy(l => l.CategoryName)
            .ToList();
    }

    private static List<BudgetLineResponse> AggregateByGroup(IReadOnlyList<BudgetLineResponse> detail)
    {
        return detail
            .GroupBy(l => (l.Block, l.GroupName))
            .Select(g =>
            {
                var planned = g.Sum(x => x.PlannedAmount);
                var actual = g.Sum(x => x.ActualAmount);
                var (percent, status) = Evaluate(planned, actual);
                return new BudgetLineResponse(
                    null, g.Key.GroupName, g.Key.GroupName, g.Key.Block,
                    planned, actual, planned - actual, percent, status, IsGroup: true);
            })
            .OrderBy(l => l.Block)
            .ThenBy(l => l.GroupName)
            .ToList();
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
        var line = await _repo.FirstOrDefaultAsync<BudgetLine>(l => l.BudgetId == budget.Id && l.CategoryId == categoryId, ct);
        if (line is null)
            _repo.Add(BudgetLine.Create(budget.Id, categoryId, planned));
        else
            line.SetPlanned(planned);
    }

    /// <summary>Linear projection for the running month; past or future months return the actual amount.</summary>
    private static decimal Project(string ym, decimal actual)
    {
        var today = DateTime.UtcNow;
        if (ym != Competence.From(today) || today.Day == 0)
            return actual;

        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        return decimal.Round(actual / today.Day * daysInMonth, 2);
    }
}
