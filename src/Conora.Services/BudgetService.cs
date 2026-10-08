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
    private sealed record AccountSum(Guid ChartAccountId, decimal Amount);

    private static readonly ChartSection[] ExpenseSections =
    [
        ChartSection.LifeProject,
        ChartSection.Essential,
        ChartSection.Social,
        ChartSection.Discount
    ];

    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly PlanService _plan;
    private readonly MonthService _months;
    private readonly ChartAccountService _chartAccounts;
    private readonly FamilyGroupService _family;

    public BudgetService(
        IFinanceRepository repo,
        IUnitOfWork uow,
        PlanService plan,
        MonthService months,
        ChartAccountService chartAccounts,
        FamilyGroupService family)
    {
        _repo = repo;
        _uow = uow;
        _plan = plan;
        _months = months;
        _chartAccounts = chartAccounts;
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

        var entries = multi
            ? await _repo.QueryAnyTenantAsync<Entry, AccountSum>(q => q
                .Where(e => userIds.Contains(e.UsuarioId)
                            && e.CompetenceYm == ym
                            && (e.Type == EntryType.Expense || e.Type == EntryType.Contribution)
                            && e.ChartAccountId != null)
                .GroupBy(e => e.ChartAccountId!.Value)
                .Select(g => new AccountSum(g.Key, g.Sum(x => x.Amount))), ct)
            : await _repo.QueryAsync<Entry, AccountSum>(q => q
                .Where(e => e.CompetenceYm == ym
                            && (e.Type == EntryType.Expense || e.Type == EntryType.Contribution)
                            && e.ChartAccountId != null)
                .GroupBy(e => e.ChartAccountId!.Value)
                .Select(g => new AccountSum(g.Key, g.Sum(x => x.Amount))), ct);

        var purchases = multi
            ? await _repo.QueryAnyTenantAsync<CardPurchase, AccountSum>(q => q
                .Where(p => userIds.Contains(p.UsuarioId) && p.CompetenceYm == ym)
                .GroupBy(p => p.ChartAccountId)
                .Select(g => new AccountSum(g.Key, g.Sum(x => x.Amount))), ct)
            : await _repo.QueryAsync<CardPurchase, AccountSum>(q => q
                .Where(p => p.CompetenceYm == ym)
                .GroupBy(p => p.ChartAccountId)
                .Select(g => new AccountSum(g.Key, g.Sum(x => x.Amount))), ct);

        var totals = new Dictionary<Guid, decimal>();
        foreach (var row in entries.Concat(purchases))
            totals[row.ChartAccountId] = totals.GetValueOrDefault(row.ChartAccountId) + row.Amount;

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

        var accounts = (await _chartAccounts.ListAsync(null, true, false, ct)).ToDictionary(c => c.Id);
        var plannedByCode = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var accountIdByCode = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            ChartAccountResponse? account = accounts.GetValueOrDefault(line.ChartAccountId);
            if (account is null && multi)
            {
                var remote = await _repo.FirstOrDefaultAnyTenantAsync<ChartAccount>(c => c.Id == line.ChartAccountId, ct);
                if (remote is not null)
                    account = ToResponse(remote);
            }

            var key = account?.Code ?? account?.Name ?? line.ChartAccountId.ToString();
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
            return (ChartAccountId: id == Guid.Empty ? Guid.NewGuid() : id, Planned: kv.Value);
        }).ToList();

        foreach (var pair in plannedPairs.Where(p => !accounts.ContainsKey(p.ChartAccountId)))
        {
            accounts[pair.ChartAccountId] = new ChartAccountResponse(
                pair.ChartAccountId, null, "—", null, ChartAccountLevel.Analytical, ChartSection.Social,
                false, true, 0, true);
        }

        var detailLines = BuildDetailLines(plannedPairs, actuals, accounts);
        var mode = budgets.FirstOrDefault()?.Mode ?? BudgetMode.Simple;
        var displayLines = mode == BudgetMode.Simple
            ? AggregateByGroup(detailLines)
            : detailLines;

        var sections = ExpenseSections.Select(section =>
        {
            var sectionLines = displayLines.Where(l => l.Section == section).ToList();
            var planned = sectionLines.Sum(l => l.PlannedAmount);
            var actual = sectionLines.Sum(l => l.ActualAmount);
            decimal? pct = spendable > 0 ? decimal.Round(actual / spendable * 100, 1) : null;
            return new BudgetSectionResponse(
                section, SystemChartAccounts.SectionLabel(section), planned, actual, pct, sectionLines);
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
            sections,
            displayLines);
    }

    public async Task<BudgetYearResponse> GetYearAsync(int year, CancellationToken ct)
    {
        if (year is < 2000 or > 2100)
            throw new ValidationException("year", "Ano inválido.");

        var months = Enumerable.Range(1, 12).Select(m => $"{year}-{m:D2}").ToList();
        var accounts = (await _chartAccounts.ListAsync(null, true, true, ct)).ToDictionary(c => c.Id);
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
                var localId = line.ChartAccountId;
                if (multi && !accounts.ContainsKey(localId))
                {
                    var remote = await _repo.FirstOrDefaultAnyTenantAsync<ChartAccount>(c => c.Id == line.ChartAccountId, ct);
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
                var section = account?.Section ?? ChartSection.Social;
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
            .ThenBy(l => l.ChartAccountName)
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

        foreach (var input in request.Lines.GroupBy(l => l.ChartAccountId).Select(g => g.Last()))
        {
            var account = await _chartAccounts.RequireAnalyticalAsync(input.ChartAccountId, ct);
            if (!ExpenseSections.Contains(account.Section))
                throw new ValidationException("lines", $"A conta '{account.Name}' não aceita orçamento de despesa.");

            await SetLineAsync(budget, input.ChartAccountId, decimal.Round(input.PlannedAmount, 2), ct);
        }

        await _uow.SaveChangesAsync(ct);
        return await GetAsync(ym, ct);
    }

    public async Task<BudgetResponse> DeleteLineAsync(string competenceYm, Guid chartAccountId, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var ym = Competence.Require(competenceYm);
        await _months.EnsureOpenAsync(ym, ct);

        var budget = await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == ym, ct)
                     ?? throw new NotFoundException($"Orçamento de {ym} não encontrado.");
        var line = await _repo.FirstOrDefaultAsync<BudgetLine>(
                       l => l.BudgetId == budget.Id && l.ChartAccountId == chartAccountId, ct)
                   ?? throw new NotFoundException("Linha de orçamento", chartAccountId);

        _repo.SoftDelete(line);
        await _uow.SaveChangesAsync(ct);
        return await GetAsync(ym, ct);
    }

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
            await SetLineAsync(budget, line.ChartAccountId, line.PlannedAmount, ct);

        await _uow.SaveChangesAsync(ct);
        return await GetAsync(ym, ct);
    }

    public async Task SyncPlannedTitheAsync(string competenceYm, decimal totalTithe, CancellationToken ct)
    {
        var ym = Competence.Require(competenceYm);
        var tithe = await _chartAccounts.GetByCodeAsync(SystemChartAccounts.Tithe, ct);
        var budget = await GetOrCreateBudgetAsync(ym, BudgetMode.Simple, ct);
        await SetLineAsync(budget, tithe.Id, Math.Max(0, decimal.Round(totalTithe, 2)), ct);
    }

    private async Task<Dictionary<Guid, decimal>> RemapActualsAsync(
        Dictionary<Guid, decimal> actuals,
        Dictionary<Guid, ChartAccountResponse> accounts,
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

            var remote = await _repo.FirstOrDefaultAnyTenantAsync<ChartAccount>(c => c.Id == accountId, ct);
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
        IReadOnlyList<(Guid ChartAccountId, decimal Planned)> lines,
        Dictionary<Guid, decimal> actuals,
        Dictionary<Guid, ChartAccountResponse> accounts)
    {
        var planned = new Dictionary<Guid, decimal>();
        foreach (var (chartAccountId, amount) in lines)
            planned[chartAccountId] = planned.GetValueOrDefault(chartAccountId) + amount;

        var parents = accounts.Values.ToDictionary(a => a.Id);
        var responses = new List<BudgetLineResponse>();
        foreach (var id in planned.Keys.Concat(actuals.Keys).Distinct())
        {
            var plan = planned.GetValueOrDefault(id);
            var actual = actuals.GetValueOrDefault(id);
            if (plan == 0 && actual == 0)
                continue;

            var account = accounts.GetValueOrDefault(id);
            var section = account?.Section ?? ChartSection.Social;
            var group = account?.ParentId is Guid pid && parents.TryGetValue(pid, out var parent)
                ? parent.Name
                : account?.Name ?? "—";
            var name = account?.Name ?? "—";
            var (percent, status) = Evaluate(plan, actual);
            responses.Add(new BudgetLineResponse(
                id, name, account?.ParentId, group, section, ChartAccountLevel.Analytical,
                plan, actual, plan - actual, percent, status, IsGroup: false));
        }

        return responses
            .OrderBy(l => l.Section)
            .ThenBy(l => l.GroupName)
            .ThenBy(l => l.ChartAccountName)
            .ToList();
    }

    private static List<BudgetLineResponse> AggregateByGroup(IReadOnlyList<BudgetLineResponse> detail)
    {
        return detail
            .GroupBy(l => (l.Section, l.GroupName, l.ParentId))
            .Select(g =>
            {
                var planned = g.Sum(x => x.PlannedAmount);
                var actual = g.Sum(x => x.ActualAmount);
                var (percent, status) = Evaluate(planned, actual);
                return new BudgetLineResponse(
                    g.Key.ParentId, g.Key.GroupName, g.Key.ParentId, g.Key.GroupName, g.Key.Section,
                    ChartAccountLevel.Group, planned, actual, planned - actual, percent, status, IsGroup: true);
            })
            .OrderBy(l => l.Section)
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

    private async Task SetLineAsync(Budget budget, Guid chartAccountId, decimal planned, CancellationToken ct)
    {
        var line = await _repo.FirstOrDefaultAsync<BudgetLine>(
            l => l.BudgetId == budget.Id && l.ChartAccountId == chartAccountId, ct);
        if (line is null)
            _repo.Add(BudgetLine.Create(budget.Id, chartAccountId, planned));
        else
            line.SetPlanned(planned);
    }

    private static decimal Project(string ym, decimal actual)
    {
        var today = DateTime.UtcNow;
        if (ym != Competence.From(today) || today.Day == 0)
            return actual;

        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        return decimal.Round(actual / today.Day * daysInMonth, 2);
    }

    private static ChartAccountResponse ToResponse(ChartAccount c)
        => new(c.Id, c.ParentId, c.Name, c.Code, c.Level, c.Section, c.IsSystem, c.IsActive, c.SortOrder, c.AcceptsPosting);
}
