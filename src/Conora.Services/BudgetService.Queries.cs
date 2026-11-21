using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed partial class BudgetService
{
public async Task<Dictionary<Guid, decimal>> GetActualsAsync(
        string competenceYm,
        CancellationToken ct,
        SharingScope? sharingScope = null)
    {
        var ym = Competence.Require(competenceYm);
        var scope = await _family.ResolveSharingScopeAsync(sharingScope, ct);
        var userIds = await _family.GetReadableUsuarioIdsAsync(ct, scope);
        var multi = userIds.Count > 1;

        var allowedCategoryIds = (await _repo.ListForUsuariosAsync<Category>(
                userIds,
                c => !c.IsPendingReview && c.Level == CategoryLevel.Analytical,
                ct))
            .Select(c => c.Id)
            .ToHashSet();

        var entries = multi
            ? await _repo.QueryForUsuariosAsync<Entry, AccountSum>(userIds, q => q
                .Where(e => e.CompetenceYm == ym
                            && BudgetActualEntryTypes.Contains(e.Type)
                            && e.CategoryId != null
                            && allowedCategoryIds.Contains(e.CategoryId.Value))
                .GroupBy(e => e.CategoryId!.Value)
                .Select(g => new AccountSum(g.Key, g.Sum(x => x.Amount))), ct)
            : await _repo.QueryAsync<Entry, AccountSum>(q => q
                .Where(e => e.CompetenceYm == ym
                            && BudgetActualEntryTypes.Contains(e.Type)
                            && e.CategoryId != null
                            && allowedCategoryIds.Contains(e.CategoryId.Value))
                .GroupBy(e => e.CategoryId!.Value)
                .Select(g => new AccountSum(g.Key, g.Sum(x => x.Amount))), ct);

        var purchases = await ListPurchasesTouchingAsync(ym, allowedCategoryIds, userIds, multi, ct);

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

/// <summary>
/// A viewer's own category keeps its id. A family peer line folds onto the local
/// category only when code, level and section all match. Display name is never a key.
/// </summary>
private async Task<Guid> ResolveLocalCategoryIdAsync(
    Guid categoryId,
    Dictionary<Guid, CategoryResponse> accounts,
    IReadOnlyList<Guid> userIds,
    bool multi,
    CancellationToken ct)
{
    if (accounts.ContainsKey(categoryId) || !multi)
        return categoryId;

    var remote = await _repo.FirstOrDefaultForUsuariosAsync<Category>(userIds, c => c.Id == categoryId, ct);
    if (remote?.Code is null)
        return categoryId;

    var local = accounts.Values.FirstOrDefault(c =>
        c.Code == remote.Code && c.Level == remote.Level && c.Section == remote.Section);
    return local?.Id ?? categoryId;
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
        var plannedById = new Dictionary<Guid, decimal>();
        var dayById = new Dictionary<Guid, int?>();

        foreach (var line in lines)
        {
            // Keep the category that owns the line. A shared display name
            // (group vs leaf, debt vs liability) must not move the amount.
            var targetId = await ResolveLocalCategoryIdAsync(line.CategoryId, accounts, userIds, multi, ct);
            plannedById[targetId] = plannedById.GetValueOrDefault(targetId) + line.PlannedAmount;
            if (line.ExpenseDay is int expenseDay)
                dayById[targetId] = expenseDay;
        }

        await ApplyLifeProjectParcelsAsync(plannedById, [ym], scope, accounts, ct);

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

        var plannedPairs = plannedById
            .Select(kv => (CategoryId: kv.Key, Planned: kv.Value, ExpenseDay: dayById.GetValueOrDefault(kv.Key)))
            .ToList();

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
        var entries = await _repo.ListForUsuariosAsync<Entry>(
            userIds,
            e => e.CompetenceYm == ym
                 && e.CategoryId != null
                 && categoryIds.Contains(e.CategoryId.Value)
                 && BudgetActualEntryTypes.Contains(e.Type),
            ct);
        var purchases = await ListPurchasesTouchingAsync(ym, categoryIds, userIds, userIds.Count > 1, ct);

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

    /// <summary>
    /// Life-project parcels are the previsto. Group projects feed the family budget,
    /// including a project owned by another member. Personal projects feed the personal budget.
    /// </summary>
    private async Task ApplyLifeProjectParcelsAsync(
        Dictionary<Guid, decimal> plannedById,
        IReadOnlyList<string> months,
        SharingScope scope,
        IReadOnlyDictionary<Guid, CategoryResponse> accounts,
        CancellationToken ct)
    {
        var parcelsByMonth = await LifeProjectParcelsByMonthAsync(months, scope, ct);
        foreach (var monthParcels in parcelsByMonth.Values)
        {
            foreach (var (categoryId, amount) in monthParcels)
            {
                if (accounts.ContainsKey(categoryId))
                    plannedById[categoryId] = amount;
            }
        }
    }

    private async Task<Dictionary<string, Dictionary<Guid, decimal>>> LifeProjectParcelsByMonthAsync(
        IReadOnlyList<string> months,
        SharingScope scope,
        CancellationToken ct)
    {
        var result = new Dictionary<string, Dictionary<Guid, decimal>>(StringComparer.Ordinal);
        if (months.Count == 0)
            return result;

        var ownerId = scope == SharingScope.Family
            ? await _family.GetBudgetOwnerUsuarioIdAsync(ct)
            : null;
        var familyConsolidated = scope == SharingScope.Family && ownerId is not null;
        var self = _family.RequireUserId();
        IReadOnlyList<LifeProject> projects;
        IReadOnlyList<Guid> userIds;
        if (familyConsolidated)
        {
            userIds = await _family.GetReadableUsuarioIdsAsync(ct, SharingScope.Family);
            var loaded = await _repo.ListForUsuariosAsync<LifeProject>(
                userIds, p => p.CategoryId != null, ct);
            projects = loaded
                .Where(p => ProjectFeedsFamilyBudget(p.Scope, p.UsuarioId == ownerId))
                .ToList();
        }
        else
        {
            userIds = [self];
            var loaded = await _repo.ListAsync<LifeProject>(p => p.CategoryId != null, ct);
            projects = loaded.Where(p => ProjectFeedsPersonalBudget(p.Scope)).ToList();
        }

        if (projects.Count == 0)
            return result;

        var localCategories = await _repo.ListAsync<Category>(
            c => c.Section == CategorySection.LifeProject, ct);
        var localById = localCategories.ToDictionary(c => c.Id);
        var remoteCategories = familyConsolidated
            ? await _repo.ListForUsuariosAsync<Category>(
                userIds, c => c.Section == CategorySection.LifeProject, ct)
            : localCategories;
        var remoteById = remoteCategories.ToDictionary(c => c.Id);
        var mapped = new List<(Guid CategoryId, decimal GoalAmount, IReadOnlyList<string> Months)>();
        foreach (var project in projects)
        {
            if (project.CategoryId is not Guid remoteId)
                continue;
            var localId = MapLifeProjectCategory(remoteId, localById, remoteById);
            if (localId is not Guid categoryId)
                continue;

            IReadOnlyList<string> projectMonths;
            try
            {
                projectMonths = Competence.RangeInclusive(
                    project.ContributionStartYm, Competence.From(project.DueDate));
            }
            catch (ValidationException)
            {
                continue;
            }

            mapped.Add((categoryId, project.GoalAmount, projectMonths));
        }

        foreach (var ym in months.Distinct(StringComparer.Ordinal))
        {
            var totals = ParcelTotalsForMonth(mapped, ym);
            if (totals.Count > 0)
                result[ym] = totals;
        }

        return result;
    }

    private static Guid? MapLifeProjectCategory(
        Guid remoteId,
        IReadOnlyDictionary<Guid, Category> localById,
        IReadOnlyDictionary<Guid, Category> remoteById)
    {
        if (localById.ContainsKey(remoteId))
            return remoteId;
        if (!remoteById.TryGetValue(remoteId, out var remote))
            return null;

        if (remote.FamilyKey is Guid key)
        {
            var byKey = localById.Values.FirstOrDefault(c => c.FamilyKey == key && c.Level == remote.Level);
            if (byKey is not null)
                return byKey.Id;
        }

        if (remote.Code is null)
            return null;

        return localById.Values.FirstOrDefault(c =>
            c.Code == remote.Code && c.Level == remote.Level && c.Section == remote.Section)?.Id;
    }
}
