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

    public async Task<Dictionary<Guid, decimal>> GetActualsAsync(
        string competenceYm,
        CancellationToken ct,
        SharingScope? sharingScope = null)
    {
        var ym = Competence.Require(competenceYm);
        var scope = await _family.ResolveSharingScopeAsync(sharingScope, ct);
        var userIds = await _family.GetReadableUsuarioIdsAsync(ct, scope);
        var multi = userIds.Count > 1;
        var until = ActualUntilUtc(ym);

        var allowedCategoryIds = (await _repo.ListForUsuariosAsync<Category>(
                userIds,
                c => !c.IsPendingReview
                     && c.Level == CategoryLevel.Analytical
                     && c.Section != CategorySection.Asset
                     && c.Section != CategorySection.Liability,
                ct))
            .Select(c => c.Id)
            .ToHashSet();

        var entries = multi
            ? await _repo.QueryForUsuariosAsync<Entry, AccountSum>(userIds, q => q
                .Where(e => e.CompetenceYm == ym
                            && e.OccurredAt <= until
                            && (e.Type == EntryType.Income
                                || e.Type == EntryType.Expense
                                || e.Type == EntryType.Contribution
                                || e.Type == EntryType.ProjectContribution)
                            && e.CategoryId != null
                            && allowedCategoryIds.Contains(e.CategoryId.Value))
                .GroupBy(e => e.CategoryId!.Value)
                .Select(g => new AccountSum(g.Key, g.Sum(x => x.Amount))), ct)
            : await _repo.QueryAsync<Entry, AccountSum>(q => q
                .Where(e => e.CompetenceYm == ym
                            && e.OccurredAt <= until
                            && (e.Type == EntryType.Income
                                || e.Type == EntryType.Expense
                                || e.Type == EntryType.Contribution
                                || e.Type == EntryType.ProjectContribution)
                            && e.CategoryId != null
                            && allowedCategoryIds.Contains(e.CategoryId.Value))
                .GroupBy(e => e.CategoryId!.Value)
                .Select(g => new AccountSum(g.Key, g.Sum(x => x.Amount))), ct);

        var purchases = await ListPurchasesTouchingAsync(ym, until, allowedCategoryIds, userIds, multi, ct);

        var totals = new Dictionary<Guid, decimal>();
        foreach (var row in entries)
            totals[row.CategoryId] = totals.GetValueOrDefault(row.CategoryId) + row.Amount;
        foreach (var purchase in purchases)
        {
            var slice = purchase.AmountInCompetence(ym);
            if (slice == 0)
                continue;
            totals[purchase.CategoryId] = totals.GetValueOrDefault(purchase.CategoryId) + slice;
        }

        return totals;
    }

    /// <summary>Purchases whose invoice plan can place a slice in the competence. The caller sums <see cref="CardPurchase.AmountInCompetence"/>.</summary>
    private async Task<IReadOnlyList<CardPurchase>> ListPurchasesTouchingAsync(
        string ym,
        DateTime until,
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
                     && p.PurchasedAt <= until
                     && categoryIds.Contains(p.CategoryId),
                ct)
            : await _repo.ListAsync<CardPurchase>(
                p => p.FirstInvoiceYm.CompareTo(windowStart) >= 0
                     && p.FirstInvoiceYm.CompareTo(ym) <= 0
                     && p.PurchasedAt <= until
                     && categoryIds.Contains(p.CategoryId),
                ct);
    }

    public async Task<BudgetResponse> GetAsync(string competenceYm, CancellationToken ct, SharingScope? sharingScope = null)
    {
        var ym = Competence.Require(competenceYm);
        var scope = await _family.ResolveSharingScopeAsync(sharingScope, ct);
        var userIds = await _family.GetReadableUsuarioIdsAsync(ct, scope);
        var multi = userIds.Count > 1;
        var ownerId = scope == SharingScope.Family
            ? await _family.GetBudgetOwnerUsuarioIdAsync(ct) ?? _family.RequireUserId()
            : _family.RequireUserId();
        var canEdit = scope != SharingScope.Family || ownerId == _family.RequireUserId();

        // Family planned = budget owner only; Personal = self.
        var plannerIds = scope == SharingScope.Family ? (IReadOnlyList<Guid>)[ownerId] : userIds;
        var budgets = multi || scope == SharingScope.Family
            ? await _repo.ListForUsuariosAsync<Budget>(plannerIds, b => b.CompetenceYm == ym, ct)
            : (await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == ym, ct, track: false) is { } one ? [one] : []);
        var budgetIds = budgets.Select(b => b.Id).ToList();
        var lines = budgetIds.Count == 0
            ? []
            : plannerIds.Count > 1 || scope == SharingScope.Family
                ? await _repo.ListForUsuariosAsync<BudgetLine>(plannerIds, l => budgetIds.Contains(l.BudgetId), ct)
                : await _repo.ListAsync<BudgetLine>(l => budgetIds.Contains(l.BudgetId), ct);

        var accounts = (await _categories.ListAsync(null, true, false, ct)).ToDictionary(c => c.Id);
        var plannedByCode = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var dayByCode = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        var accountIdByCode = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            CategoryResponse? account = accounts.GetValueOrDefault(line.CategoryId);
            if (account is null && multi)
            {
                var remote = await _repo.FirstOrDefaultForUsuariosAsync<Category>(userIds, c => c.Id == line.CategoryId, ct);
                if (remote is not null)
                    account = ToResponse(remote);
            }

            var key = account?.Code ?? account?.Name ?? line.CategoryId.ToString();
            plannedByCode[key] = plannedByCode.GetValueOrDefault(key) + line.PlannedAmount;
            if (line.ExpenseDay is int expenseDay)
                dayByCode[key] = expenseDay;
            if (account is not null)
            {
                var local = accounts.Values.FirstOrDefault(c =>
                    (account.Code is not null && c.Code == account.Code) || c.Name == account.Name);
                if (local is not null)
                    accountIdByCode[key] = local.Id;
            }
        }

        var actuals = await GetActualsAsync(ym, ct, scope);
        if (multi)
            actuals = await RemapActualsAsync(actuals, accounts, userIds, ct);

        // Never expose peer IncomeSource names; diagnosis remains personal.
        var selfId = _family.RequireUserId();
        var ownSources = await _repo.ListAsync<IncomeSource>(s => s.CompetenceYm == ym, ct);
        var sources = ownSources
            .OrderBy(s => s.Name)
            .Select(s => new BudgetIncomeSourceResponse(s.Id, s.Name, s.NetSpendable))
            .ToList();
        var spendable = ownSources.Sum(s => s.NetSpendable);
        if (scope == SharingScope.Family && multi)
        {
            var peerIds = userIds.Where(id => id != selfId).ToList();
            if (peerIds.Count > 0)
            {
                // Family spendable may include peers' net totals (anonymous sum only).
                spendable += (await _repo.ListForUsuariosAsync<IncomeSource>(
                        peerIds, s => s.CompetenceYm == ym, ct))
                    .Sum(s => s.NetSpendable);
            }
        }

        var plannedPairs = plannedByCode.Select(kv =>
        {
            var id = accountIdByCode.GetValueOrDefault(kv.Key);
            return (CategoryId: id == Guid.Empty ? Guid.NewGuid() : id, Planned: kv.Value, ExpenseDay: dayByCode.GetValueOrDefault(kv.Key));
        }).ToList();

        foreach (var pair in plannedPairs.Where(p => !accounts.ContainsKey(p.CategoryId)))
        {
            accounts[pair.CategoryId] = new CategoryResponse(
                pair.CategoryId, null, "—", null, null, CategoryLevel.Analytical, CategorySection.Social,
                false, true, 0, true, SharingScope.Family, IsPendingReview: false);
        }

        var self = await _family.FindUserByIdAsync(selfId, ct);
        var attentionPercent = self?.BudgetAlertPercent ?? 70;
        var detailLines = BuildDetailLines(plannedPairs, actuals, accounts, attentionPercent);
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
        var receivedIncome = await GetReceivedIncomeAsync(ym, userIds, multi, scope, ct);
        var monthResult = MonthBalance.FromReceived(
            receivedIncome,
            sections.First(s => s.Section == CategorySection.Discount).ActualAmount,
            sections.First(s => s.Section == CategorySection.LifeProject).ActualAmount,
            sections.First(s => s.Section == CategorySection.Essential).ActualAmount,
            sections.First(s => s.Section == CategorySection.Social).ActualAmount);
        return new BudgetResponse(
            ym,
            mode,
            totalPlanned,
            totalActual,
            Project(ym, totalActual),
            spendable,
            receivedIncome,
            monthResult,
            sources,
            sections,
            displayLines,
            scope,
            canEdit,
            scope == SharingScope.Family ? ownerId : null);
    }

    public async Task<BudgetYearResponse> GetYearAsync(
        int year,
        CancellationToken ct,
        SharingScope? sharingScope = null)
    {
        if (year is < 2000 or > 2100)
            throw new ValidationException("year", "Ano inválido.");

        var months = Enumerable.Range(1, 12).Select(m => $"{year}-{m:D2}").ToList();
        var accounts = (await _categories.ListAsync(null, true, true, ct)).ToDictionary(c => c.Id);
        var plannedByMonth = new Dictionary<string, Dictionary<Guid, decimal>>();
        var actualByMonth = new Dictionary<string, Dictionary<Guid, decimal>>();

        var scope = await _family.ResolveSharingScopeAsync(sharingScope, ct);
        var userIds = await _family.GetReadableUsuarioIdsAsync(ct, scope);
        var multi = userIds.Count > 1;
        var ownerId = scope == SharingScope.Family
            ? await _family.GetBudgetOwnerUsuarioIdAsync(ct) ?? _family.RequireUserId()
            : _family.RequireUserId();
        // Family planned = owner only; Personal = self. Never sum peers' personal budgets.
        var plannerIds = scope == SharingScope.Family ? (IReadOnlyList<Guid>)[ownerId] : userIds;
        foreach (var ym in months)
        {
            var budgets = multi || scope == SharingScope.Family
                ? await _repo.ListForUsuariosAsync<Budget>(plannerIds, b => b.CompetenceYm == ym, ct)
                : (await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == ym, ct, track: false) is { } one ? [one] : []);
            var budgetIds = budgets.Select(b => b.Id).ToList();
            var lines = budgetIds.Count == 0
                ? []
                : plannerIds.Count > 1 || scope == SharingScope.Family
                    ? await _repo.ListForUsuariosAsync<BudgetLine>(plannerIds, l => budgetIds.Contains(l.BudgetId), ct)
                    : await _repo.ListAsync<BudgetLine>(l => budgetIds.Contains(l.BudgetId), ct);

            var planned = new Dictionary<Guid, decimal>();
            foreach (var line in lines)
            {
                var localId = line.CategoryId;
                Category? remote = null;
                if (!accounts.ContainsKey(localId)
                    && (multi || scope == SharingScope.Family))
                {
                    remote = await _repo.FirstOrDefaultForUsuariosAsync<Category>(
                        plannerIds, c => c.Id == line.CategoryId, ct);
                    var match = remote is null
                        ? null
                        : accounts.Values.FirstOrDefault(c =>
                            (remote.Code is not null && c.Code == remote.Code) || c.Name == remote.Name);
                    if (match is not null)
                        localId = match.Id;
                }

                // Skip lines that belong to the other sharing scope.
                var lineScope = accounts.TryGetValue(localId, out var localCat)
                    ? localCat.SharingScope
                    : remote?.SharingScope;
                if (lineScope is SharingScope ls && ls != scope)
                    continue;

                planned[localId] = planned.GetValueOrDefault(localId) + line.PlannedAmount;
            }

            plannedByMonth[ym] = planned;
            actualByMonth[ym] = await GetActualsAsync(ym, ct, scope);
            if (multi)
                actualByMonth[ym] = await RemapActualsAsync(actualByMonth[ym], accounts, userIds, ct);
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

        var receivedByMonth = await GetReceivedIncomeByMonthAsync(months, userIds, multi, ct);
        var totals = months.Select(ym =>
        {
            var received = receivedByMonth.GetValueOrDefault(ym);
            var monthResult = MonthBalance.FromReceived(
                received,
                ActualOf(ym, CategorySection.Discount),
                ActualOf(ym, CategorySection.LifeProject),
                ActualOf(ym, CategorySection.Essential),
                ActualOf(ym, CategorySection.Social));
            return new BudgetYearMonthCell(
                ym,
                plannedByMonth[ym].Values.Sum(),
                actualByMonth[ym].Values.Sum(),
                received,
                monthResult);
        }).ToList();

        decimal ActualOf(string monthYm, CategorySection section)
        {
            decimal sum = 0;
            foreach (var (id, amount) in actualByMonth[monthYm])
            {
                if (accounts.TryGetValue(id, out var account) && account.Section == section)
                    sum += amount;
            }

            return sum;
        }

        return new BudgetYearResponse(year, months, yearLines, totals);
    }

    public async Task<BudgetResponse> UpsertAsync(
        string competenceYm,
        UpsertBudgetRequest request,
        CancellationToken ct,
        SharingScope? sharingScope = null)
    {
        await _plan.EnsureWritableAsync(ct);
        var scope = await _family.ResolveSharingScopeAsync(sharingScope, ct);
        if (scope == SharingScope.Family)
            await _family.EnsureCanEditGroupBudgetAsync(ct);

        var ym = Competence.Require(competenceYm);
        await _months.EnsureOpenAsync(ym, ct);

        var budget = await GetOrCreateBudgetAsync(ym, BudgetMode.Detailed, ct);
        budget.SetMode(BudgetMode.Detailed);

        foreach (var input in request.Lines.GroupBy(l => l.CategoryId).Select(g => g.Last()))
        {
            var account = await _categories.RequireAnalyticalAsync(input.CategoryId, ct);
            if (!BudgetSections.Contains(account.Section))
                throw new ValidationException("lines", $"A conta '{account.Name}' não aceita orçamento (somente receita e despesa).");
            await EnsureManualPlanAllowedAsync(input.CategoryId, account, ct);

            await SetLineAsync(budget, input.CategoryId, decimal.Round(input.PlannedAmount, 2), ct, input.ExpenseDay, updateDay: true);
        }

        await _uow.SaveChangesAsync(ct);
        return await GetAsync(ym, ct, scope);
    }

    public async Task<BudgetResponse> DeleteLineAsync(string competenceYm, Guid categoryId, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var account = await _categories.RequireAnalyticalAsync(categoryId, ct);
        if (account.SharingScope == SharingScope.Family
            && await _family.GetBudgetOwnerUsuarioIdAsync(ct) is not null)
            await _family.EnsureCanEditGroupBudgetAsync(ct);

        var ym = Competence.Require(competenceYm);
        await _months.EnsureOpenAsync(ym, ct);
        await EnsureManualPlanAllowedAsync(categoryId, account, ct);

        var budget = await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == ym, ct)
                     ?? throw new NotFoundException($"Orçamento de {ym} não encontrado.");
        var line = await _repo.FirstOrDefaultAsync<BudgetLine>(
                       l => l.BudgetId == budget.Id && l.CategoryId == categoryId, ct)
                   ?? throw new NotFoundException("Linha de orçamento", categoryId);

        _repo.SoftDelete(line);
        await _uow.SaveChangesAsync(ct);
        return await GetAsync(ym, ct);
    }

    public async Task<CategoryMemberBreakdownResponse> GetCategoryBreakdownAsync(
        string competenceYm,
        Guid categoryId,
        CancellationToken ct,
        SharingScope? sharingScope = null)
    {
        var ym = Competence.Require(competenceYm);
        var scope = await _family.ResolveSharingScopeAsync(sharingScope, ct);
        var userIds = await _family.GetReadableUsuarioIdsAsync(ct, scope);
        var local = await _repo.FirstOrDefaultAsync<Category>(c => c.Id == categoryId, ct, track: false)
                    ?? throw new NotFoundException("Conta", categoryId);

        var peerCategories = await _repo.ListForUsuariosAsync<Category>(
            userIds,
            c => !c.IsPendingReview
                 && c.Level == CategoryLevel.Analytical
                 && ((local.Code != null && c.Code == local.Code) || c.Name == local.Name),
            ct);
        var categoryIds = peerCategories.Select(c => c.Id).ToHashSet();
        var until = ActualUntilUtc(ym);
        var entries = await _repo.ListForUsuariosAsync<Entry>(
            userIds,
            e => e.CompetenceYm == ym
                 && e.OccurredAt <= until
                 && e.CategoryId != null
                 && categoryIds.Contains(e.CategoryId.Value)
                 && (e.Type == EntryType.Income
                     || e.Type == EntryType.Expense
                     || e.Type == EntryType.Contribution
                     || e.Type == EntryType.ProjectContribution),
            ct);
        var purchases = await ListPurchasesTouchingAsync(ym, until, categoryIds, userIds, userIds.Count > 1, ct);

        var byUser = userIds.ToDictionary(id => id, _ => 0m);
        foreach (var entry in entries)
            byUser[entry.UsuarioId] = byUser.GetValueOrDefault(entry.UsuarioId) + entry.Amount;
        foreach (var purchase in purchases)
            byUser[purchase.UsuarioId] = byUser.GetValueOrDefault(purchase.UsuarioId) + purchase.AmountInCompetence(ym);

        var names = userIds.ToDictionary(id => id, id => id == _family.RequireUserId() ? "Você" : "Membro");
        var group = await _family.GetGroupAsync(ct);
        foreach (var member in group.Members)
            names[member.UsuarioId] = member.IsSelf ? $"{member.Name} (você)" : member.Name;

        var rows = byUser
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new CategoryMemberBreakdownRow(kv.Key, names.GetValueOrDefault(kv.Key, "—"), kv.Value))
            .ToList();
        return new CategoryMemberBreakdownResponse(
            ym,
            local.Code ?? local.Id.ToString(),
            local.Name,
            scope,
            rows.Sum(r => r.Amount),
            rows);
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
            if (await HasLifeProjectAsync(line.CategoryId, ct))
                continue;
            await SetLineAsync(budget, line.CategoryId, line.PlannedAmount, ct, line.ExpenseDay, updateDay: true);
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
        await EnsureManualPlanAllowedAsync(request.CategoryId, account, ct);

        await _months.EnsureOpenAsync(startYm, ct);
        var startBudget = await GetOrCreateBudgetAsync(startYm, BudgetMode.Detailed, ct);
        var amount = request.PlannedAmount is decimal explicitAmount
            ? decimal.Round(explicitAmount, 2)
            : await GetPlannedAsync(startBudget.Id, request.CategoryId, ct);
        if (amount <= 0)
            throw new ValidationException("categoryId", "Informe um previsto neste mês antes de repetir.");

        if (request.PlannedAmount is not null)
            await SetLineAsync(startBudget, request.CategoryId, amount, ct);

        var expenseDay = (await _repo.FirstOrDefaultAsync<BudgetLine>(
            l => l.BudgetId == startBudget.Id && l.CategoryId == request.CategoryId, ct, track: false))?.ExpenseDay;

        for (var i = 1; i < request.MonthCount; i++)
        {
            var ym = Competence.AddMonths(startYm, i);
            await _months.EnsureOpenAsync(ym, ct);
            var budget = await GetOrCreateBudgetAsync(ym, BudgetMode.Detailed, ct);
            if (!request.Overwrite && await GetPlannedAsync(budget.Id, request.CategoryId, ct) > 0)
                continue;
            await SetLineAsync(budget, request.CategoryId, amount, ct, expenseDay, updateDay: true);
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
        await EnsureManualPlanAllowedAsync(request.CategoryId, account, ct);

        var total = decimal.Round(request.TotalAmount, 2);
        var startBudget = await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == startYm, ct, track: false);
        var expenseDay = startBudget is null
            ? null
            : (await _repo.FirstOrDefaultAsync<BudgetLine>(
                l => l.BudgetId == startBudget.Id && l.CategoryId == request.CategoryId, ct, track: false))?.ExpenseDay;

        var slots = new List<Budget>();
        for (var i = 0; i < request.InstallmentCount; i++)
        {
            var ym = Competence.AddMonths(startYm, i);
            await _months.EnsureOpenAsync(ym, ct);
            var budget = await GetOrCreateBudgetAsync(ym, BudgetMode.Detailed, ct);
            if (!request.Overwrite && await GetPlannedAsync(budget.Id, request.CategoryId, ct) > 0)
                continue;
            slots.Add(budget);
        }

        if (slots.Count == 0)
            throw new ValidationException("installmentCount", "Todos os meses já têm previsto. Substitua para parcelar de novo.");

        var each = decimal.Round(total / slots.Count, 2);
        var allocated = 0m;
        for (var i = 0; i < slots.Count; i++)
        {
            var value = i == slots.Count - 1 ? total - allocated : each;
            allocated += value;
            await SetLineAsync(slots[i], request.CategoryId, value, ct, expenseDay, updateDay: true);
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
        SharingScope scope,
        CancellationToken ct)
    {
        var until = ActualUntilUtc(ym);
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
                     && e.OccurredAt <= until
                     && (
                         (e.CategoryId != null && allowedCategoryIds.Contains(e.CategoryId.Value))
                         || (e.UsuarioId == selfId && e.CategoryId == null)),
                ct)).Sum(e => e.Amount);
        }

        return await _repo.SumAsync<Entry>(
            e => e.CompetenceYm == ym
                 && e.Type == EntryType.Income
                 && e.OccurredAt <= until
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
        {
            if (entry.OccurredAt > ActualUntilUtc(entry.CompetenceYm))
                continue;
            totals[entry.CompetenceYm] = totals.GetValueOrDefault(entry.CompetenceYm) + entry.Amount;
        }

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
