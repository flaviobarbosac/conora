using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed partial class BudgetService
{
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
}
