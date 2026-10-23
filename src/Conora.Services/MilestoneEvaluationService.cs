using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Microsoft.Extensions.Logging;

namespace Conora.Services;

/// <summary>Nightly (and on-demand) evaluation of Ian maturity milestones 8–30. Grants are never revoked.</summary>
public sealed class MilestoneEvaluationService
{
    private readonly IUserRepository _users;
    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenant;
    private readonly CategoryService _categories;
    private readonly BudgetService _budgets;
    private readonly PatrimonyService _patrimony;
    private readonly ILogger<MilestoneEvaluationService> _logger;

    public MilestoneEvaluationService(
        IUserRepository users,
        IFinanceRepository repo,
        IUnitOfWork uow,
        ITenantContext tenant,
        CategoryService categories,
        BudgetService budgets,
        PatrimonyService patrimony,
        ILogger<MilestoneEvaluationService> logger)
    {
        _users = users;
        _repo = repo;
        _uow = uow;
        _tenant = tenant;
        _categories = categories;
        _budgets = budgets;
        _patrimony = patrimony;
        _logger = logger;
    }

    public async Task RunAllUsersAsync(CancellationToken ct)
    {
        var ids = await _users.ListActiveUserIdsAsync(ct);
        foreach (var id in ids)
        {
            try
            {
                await EvaluateUserAsync(id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Milestone evaluation failed for user {UserId}", id);
            }
        }
    }

    public async Task EvaluateUserAsync(Guid usuarioId, CancellationToken ct)
    {
        _tenant.Set(usuarioId);
        var existing = await _repo.ListAsync<UserMilestone>(null, ct);
        var grantedKeys = existing.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        var grantedAges = existing.Select(m => m.Age).ToHashSet();

        foreach (var def in MilestoneCatalog.Ordered)
        {
            if (grantedKeys.Contains(def.Key))
                continue;

            // Figure advances only in order: previous age must already be granted (except the first).
            var previous = MilestoneCatalog.Ordered.TakeWhile(m => m.Age < def.Age).LastOrDefault();
            if (previous is not null && !grantedAges.Contains(previous.Age))
                break;

            var (met, at) = await IsMetAsync(def, ct);
            if (!met)
                continue;

            _repo.Add(UserMilestone.Grant(def.Key, def.Age, def.Points, at ?? DateTime.UtcNow));
            grantedKeys.Add(def.Key);
            grantedAges.Add(def.Age);
            _logger.LogInformation("Granted milestone {Key} age {Age} (+{Points}) to {UserId}", def.Key, def.Age, def.Points, usuarioId);
        }

        await _uow.SaveChangesAsync(ct);
    }

    private async Task<(bool Met, DateTime? At)> IsMetAsync(MilestoneDefinition def, CancellationToken ct)
        => def.Age switch
        {
            8 => await MetBudgetHalfFilledAsync(ct),
            10 => await MetFirstMonthClosedAsync(ct),
            12 => await MetThreeMonthsInBudgetAsync(ct),
            15 => await MetPaymentOnTimeAsync(ct),
            18 => await MetReserveStartedAsync(ct),
            22 => await MetReserveCompleteAsync(ct),
            30 => await MetProjectProgressAsync(ct),
            _ => (false, null)
        };

    /// <summary>Age 8: ≥50% of active analytical categories have a planned amount in some competence.</summary>
    private async Task<(bool, DateTime?)> MetBudgetHalfFilledAsync(CancellationToken ct)
    {
        var active = (await _categories.ListAsync(null, includeInactive: false, analyticalOnly: true, ct))
            .Where(c => !c.IsPendingReview)
            .ToList();
        if (active.Count == 0)
            return (false, null);

        var budgets = await _repo.ListAsync<Budget>(null, ct);
        foreach (var budget in budgets.OrderBy(b => b.CompetenceYm))
        {
            var lines = await _repo.ListAsync<BudgetLine>(l => l.BudgetId == budget.Id, ct);
            var filled = active.Count(c => lines.Any(l => l.CategoryId == c.Id && Math.Abs(l.PlannedAmount) > 0));
            if (filled * 2 >= active.Count)
                return (true, budget.CreatedAt);
        }

        return (false, null);
    }

    private async Task<(bool, DateTime?)> MetFirstMonthClosedAsync(CancellationToken ct)
    {
        var first = (await _repo.ListAsync<MonthLock>(m => m.IsClosed, ct))
            .OrderBy(m => m.ClosedAt)
            .FirstOrDefault();
        return first is null ? (false, null) : (true, first.ClosedAt);
    }

    private async Task<(bool, DateTime?)> MetThreeMonthsInBudgetAsync(CancellationToken ct)
    {
        var closed = (await _repo.ListAsync<MonthLock>(m => m.IsClosed, ct))
            .OrderBy(m => m.CompetenceYm)
            .ToList();
        if (closed.Count < 3)
            return (false, null);

        string? previous = null;
        var streak = 0;
        DateTime? streakAt = null;
        foreach (var row in closed)
        {
            var consecutive = previous is null || Competence.AddMonths(previous, 1) == row.CompetenceYm;
            if (!consecutive)
                streak = 0;
            if (await IsWithinBudgetValueAsync(row.CompetenceYm, ct))
            {
                streak++;
                if (streak >= 3)
                    return (true, row.ClosedAt ?? streakAt);
                streakAt = row.ClosedAt;
            }
            else
            {
                streak = 0;
            }

            previous = row.CompetenceYm;
        }

        return (false, null);
    }

    /// <summary>Age 15: first closed month where every expense with ExpenseDay was paid on that day (not value limit).</summary>
    private async Task<(bool, DateTime?)> MetPaymentOnTimeAsync(CancellationToken ct)
    {
        var closed = (await _repo.ListAsync<MonthLock>(m => m.IsClosed, ct))
            .OrderBy(m => m.CompetenceYm)
            .ToList();

        foreach (var row in closed)
        {
            if (!await IsPaymentOnExpenseDayAsync(row.CompetenceYm, ct))
                continue;
            return (true, row.ClosedAt);
        }

        return (false, null);
    }

    private async Task<(bool, DateTime?)> MetReserveStartedAsync(CancellationToken ct)
    {
        var reserve = await _patrimony.GetReserveAsync(null, ct);
        return reserve.ReserveBalance > 0 ? (true, DateTime.UtcNow) : (false, null);
    }

    private async Task<(bool, DateTime?)> MetReserveCompleteAsync(CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(_tenant.UsuarioId!.Value, ct);
        var target = user?.ReserveTargetMonths ?? 6;
        var reserve = await _patrimony.GetReserveAsync(null, ct, target);
        if (reserve.MonthlyEssentialAverage <= 0)
            return (false, null);
        return reserve.MonthsCovered >= reserve.TargetMonths ? (true, DateTime.UtcNow) : (false, null);
    }

    private async Task<(bool, DateTime?)> MetProjectProgressAsync(CancellationToken ct)
    {
        var projects = await _repo.ListAsync<LifeProject>(null, ct);
        var advanced = projects.FirstOrDefault(p => p.AccumulatedAmount > 0);
        return advanced is null ? (false, null) : (true, advanced.UpdatedAt);
    }

    private async Task<bool> IsWithinBudgetValueAsync(string ym, CancellationToken ct)
    {
        var budget = await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == ym, ct, track: false);
        if (budget is null)
            return false;
        var lines = await _repo.ListAsync<BudgetLine>(l => l.BudgetId == budget.Id, ct);
        if (lines.Count == 0)
            return false;
        var planned = lines.Sum(l => Math.Abs(l.PlannedAmount));
        if (planned <= 0)
            return false;
        var actuals = await _budgets.GetActualsAsync(ym, ct);
        var expenseIds = (await _categories.ListAsync(null, true, true, ct))
            .Where(c => c.Section is CategorySection.Essential or CategorySection.Social or CategorySection.LifeProject
                or CategorySection.Discount)
            .Select(c => c.Id)
            .ToHashSet();
        var actual = actuals.Where(kv => expenseIds.Contains(kv.Key)).Sum(kv => Math.Abs(kv.Value));
        return actual <= planned;
    }

    private async Task<bool> IsPaymentOnExpenseDayAsync(string ym, CancellationToken ct)
    {
        var budget = await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == ym, ct, track: false);
        if (budget is null)
            return false;

        var lines = (await _repo.ListAsync<BudgetLine>(l => l.BudgetId == budget.Id && l.ExpenseDay != null, ct))
            .Where(l => l.ExpenseDay is >= 1 and <= 31)
            .ToList();
        if (lines.Count == 0)
            return false;

        var year = int.Parse(ym[..4]);
        var month = int.Parse(ym[5..7]);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var entries = await _repo.ListAsync<Entry>(
            e => e.CompetenceYm == ym && e.Type == EntryType.Expense && e.CategoryId != null, ct);

        foreach (var line in lines)
        {
            var day = Math.Min(line.ExpenseDay!.Value, daysInMonth);
            var due = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
            var paid = entries.Where(e => e.CategoryId == line.CategoryId).ToList();
            if (paid.Count == 0)
                return false;
            if (paid.Any(e => e.OccurredAt.Date != due.Date))
                return false;
        }

        return true;
    }
}
