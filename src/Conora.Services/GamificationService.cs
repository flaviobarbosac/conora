using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class GamificationService
{
    private readonly IFinanceRepository _repo;
    private readonly IFamilyGroupRepository _family;
    private readonly ITenantContext _tenant;
    private readonly FamilyGroupService _familyService;
    private readonly CategoryService _categories;
    private readonly DashboardService _dashboard;
    private readonly BudgetService _budgets;
    private readonly LifeProjectService _projects;
    private readonly PatrimonyService _patrimony;

    public GamificationService(
        IFinanceRepository repo,
        IFamilyGroupRepository family,
        ITenantContext tenant,
        FamilyGroupService familyService,
        CategoryService categories,
        DashboardService dashboard,
        BudgetService budgets,
        LifeProjectService projects,
        PatrimonyService patrimony)
    {
        _repo = repo;
        _family = family;
        _tenant = tenant;
        _familyService = familyService;
        _categories = categories;
        _dashboard = dashboard;
        _budgets = budgets;
        _projects = projects;
        _patrimony = patrimony;
    }

    public async Task<JourneyResponse> GetJourneyAsync(CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        var steps = await BuildJourneyStepsAsync(ct);
        var next = steps.FirstOrDefault(s => !s.Completed);
        return new JourneyResponse(user.QuietMode, steps, next);
    }

    public async Task<AchievementsResponse> GetAchievementsAsync(CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        return new AchievementsResponse(user.QuietMode, await BuildAchievementsAsync(ct));
    }

    public async Task<HomeProgressResponse> GetHomeProgressAsync(string? competenceYm, CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        var ym = string.IsNullOrWhiteSpace(competenceYm) ? Competence.From(DateTime.UtcNow) : Competence.Require(competenceYm);
        var journey = await BuildJourneyStepsAsync(ct);
        var next = user.QuietMode ? null : journey.FirstOrDefault(s => !s.Completed);
        var reserve = await BuildReserveThermometerAsync(ym, user.ReserveTargetMonths, ct);
        var budgetClose = await BuildBudgetCloseAsync(ym, ct);
        var projects = await BuildProjectProgressAsync(ct);
        var achievements = user.QuietMode
            ? Array.Empty<AchievementResponse>()
            : await BuildAchievementsAsync(ct);
        return new HomeProgressResponse(user.QuietMode, next, reserve, budgetClose, projects, achievements);
    }

    public async Task<MonthCloseSummaryResponse> GetMonthCloseSummaryAsync(string competenceYm, CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        var ym = Competence.Require(competenceYm);
        var lockRow = await _repo.FirstOrDefaultAsync<MonthLock>(m => m.CompetenceYm == ym, ct, track: false);
        var dash = await _dashboard.GetAsync(ym, ct);
        var within = await IsWithinBudgetAsync(ym, ct);
        var journey = await BuildJourneyStepsAsync(ct);
        var next = user.QuietMode ? null : journey.FirstOrDefault(s => !s.Completed);
        string? celebration = null;
        if (lockRow is { IsClosed: true } && !user.QuietMode)
        {
            celebration = within
                ? "Mês fechado dentro do orçamento. Bom ritmo."
                : "Mês fechado. O próximo passo continua disponível.";
        }

        return new MonthCloseSummaryResponse(
            ym,
            lockRow?.IsClosed ?? false,
            lockRow?.ClosedAt,
            lockRow?.ReopenReason,
            lockRow?.ClosedByUsuarioId,
            dash.IncomeTotal,
            dash.ExpenseTotal,
            dash.Result,
            within,
            user.QuietMode,
            celebration,
            next);
    }

    public async Task<YearRetrospectiveResponse> GetYearRetrospectiveAsync(int year, CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        if (year is < 2000 or > 2100)
            throw new ValidationException("year", "Ano inválido.");

        var includeValues = user.IncludeAbsoluteValuesInRetrospective;
        var months = new List<YearRetrospectiveMonth>();
        var closed = 0;
        var within = 0;
        for (var month = 1; month <= 12; month++)
        {
            var ym = $"{year:D4}-{month:D2}";
            var lockRow = await _repo.FirstOrDefaultAsync<MonthLock>(m => m.CompetenceYm == ym, ct, track: false);
            var isClosed = lockRow is { IsClosed: true };
            var withinBudget = isClosed && await IsWithinBudgetAsync(ym, ct);
            if (isClosed) closed++;
            if (withinBudget) within++;

            decimal? income = null, expense = null, result = null;
            if (includeValues)
            {
                var dash = await _dashboard.GetAsync(ym, ct);
                income = dash.IncomeTotal;
                expense = dash.ExpenseTotal;
                result = dash.Result;
            }

            months.Add(new YearRetrospectiveMonth(ym, isClosed, withinBudget, income, expense, result));
        }

        var achievements = user.QuietMode
            ? Array.Empty<AchievementResponse>()
            : await BuildAchievementsAsync(ct);

        return new YearRetrospectiveResponse(
            year,
            user.QuietMode,
            includeValues,
            closed,
            within,
            months,
            achievements);
    }

    public async Task<ReserveThermometerResponse> GetReserveThermometerAsync(string? competenceYm, CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        var ym = string.IsNullOrWhiteSpace(competenceYm) ? Competence.From(DateTime.UtcNow) : Competence.Require(competenceYm);
        return await BuildReserveThermometerAsync(ym, user.ReserveTargetMonths, ct);
    }

    private async Task<IReadOnlyList<JourneyStepResponse>> BuildJourneyStepsAsync(CancellationToken ct)
    {
        var hasBudget = await _repo.AnyAsync<Budget>(_ => true, ct);
        var hasMonthlyDebt = await HasMonthlyDebtAsync(ct);
        var hasLiability = await HasLiabilityAsync(ct);
        var hasProject = await _repo.AnyAsync<LifeProject>(_ => true, ct);

        var defs = new (string Key, string Title, string Route, bool Done)[]
        {
            ("budget", "Orçamento", "/orcamento", hasBudget),
            ("debt", "Dívida mensal", "/orcamento", hasMonthlyDebt),
            ("patrimony", "Patrimônio · passivo", "/patrimonio", hasLiability),
            ("projects", "Projetos de vida", "/projetos", hasProject),
        };

        var nextKey = defs.FirstOrDefault(d => !d.Done).Key;
        return defs.Select(d => new JourneyStepResponse(d.Key, d.Title, d.Route, d.Done, d.Key == nextKey && !d.Done)).ToList();
    }

    private async Task<bool> HasMonthlyDebtAsync(CancellationToken ct)
    {
        var debtCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "ESS_LOAN", "ESS_CONSIGNED", "ESS_OVERDRAFT", "ESS_CAR_FIN", "ESS_MORTGAGE",
            "ESS_RENEG", "ESS_CARD_INST", "ESS_CARD_INT", "ESS_INTEREST", "ESS_FINES",
            "ESS_BANK_FEE", "ESS_IOF", "ESS_DEBT_OTHER",
        };
        var categories = await _repo.ListAsync<Category>(
            c => c.Code != null && debtCodes.Contains(c.Code!), ct, track: false);
        if (categories.Count == 0)
            return false;
        var ids = categories.Select(c => c.Id).ToHashSet();
        return await _repo.AnyAsync<BudgetLine>(l => ids.Contains(l.CategoryId) && l.PlannedAmount > 0, ct);
    }

    private async Task<bool> HasLiabilityAsync(CancellationToken ct)
    {
        var liabilityCategories = await _repo.ListAsync<Category>(
            c => c.Section == CategorySection.Liability, ct, track: false);
        if (liabilityCategories.Count == 0)
            return false;
        var ids = liabilityCategories.Select(c => c.Id).ToHashSet();
        return await _repo.AnyAsync<PatrimonyItem>(p => ids.Contains(p.CategoryId) && p.Amount > 0, ct);
    }

    private async Task<IReadOnlyList<AchievementResponse>> BuildAchievementsAsync(CancellationToken ct)
    {
        // Persisted grants from the nightly job win; live checks fill the UI before the job runs.
        var granted = (await _repo.ListAsync<UserMilestone>(null, ct))
            .ToDictionary(m => m.Key, StringComparer.Ordinal);

        var list = new List<AchievementResponse>();
        foreach (var def in MilestoneCatalog.Ordered)
        {
            if (granted.TryGetValue(def.Key, out var row))
            {
                list.Add(new(def.Key, def.Title, $"{def.Points} pontos · idade {def.Age}.", true, row.GrantedAt, def.Points, def.Age));
                continue;
            }

            var (earned, at) = await LiveMilestoneHintAsync(def.Key, ct);
            list.Add(new(def.Key, def.Title, $"{def.Points} pontos · idade {def.Age}.", earned, at, def.Points, def.Age));
        }

        return list;
    }

    private async Task<(bool Earned, DateTime? At)> LiveMilestoneHintAsync(string key, CancellationToken ct)
    {
        switch (key)
        {
            case "first-month-closed":
            {
                var first = (await _repo.ListAsync<MonthLock>(m => m.IsClosed, ct)).OrderBy(m => m.ClosedAt).FirstOrDefault();
                return (first is not null, first?.ClosedAt);
            }
            case "three-months-in-budget":
            {
                var closed = (await _repo.ListAsync<MonthLock>(m => m.IsClosed, ct)).OrderBy(m => m.CompetenceYm).ToList();
                var streak = await LongestWithinBudgetStreakAsync(closed, ct);
                var at = streak >= 3
                    ? closed.Select(m => m.ClosedAt).Where(a => a is not null).OrderBy(a => a).Skip(2).FirstOrDefault()
                    : null;
                return (streak >= 3, at);
            }
            case "reserve-complete":
            {
                var reserve = await BuildReserveThermometerAsync(Competence.From(DateTime.UtcNow), (await RequireUserAsync(ct)).ReserveTargetMonths, ct);
                return (reserve.MonthsCovered >= reserve.TargetMonths && reserve.MonthlyEssentialAverage > 0, null);
            }
            case "reserve-started":
            {
                var reserve = await BuildReserveThermometerAsync(Competence.From(DateTime.UtcNow), (await RequireUserAsync(ct)).ReserveTargetMonths, ct);
                return (reserve.ReserveBalance > 0, null);
            }
            case "project-progress":
            {
                var advanced = (await _repo.ListAsync<LifeProject>(null, ct)).FirstOrDefault(p => p.AccumulatedAmount > 0);
                return (advanced is not null, advanced?.UpdatedAt);
            }
            case "budget-half-filled":
                return (await IsHalfBudgetFilledAsync(ct), null);
            case "payment-on-time":
            {
                var ym = await FirstPaymentOnTimeYmAsync(ct);
                return (ym is not null, null);
            }
            default:
                return (false, null);
        }
    }

    private async Task<bool> IsHalfBudgetFilledAsync(CancellationToken ct)
    {
        var active = await _repo.ListAsync<Category>(
            c => c.Level == CategoryLevel.Analytical && c.IsActive && !c.IsPendingReview
                 && (c.Section == CategorySection.Essential
                     || c.Section == CategorySection.Social
                     || c.Section == CategorySection.Discount
                     || c.Section == CategorySection.LifeProject
                     || c.Section == CategorySection.Income),
            ct,
            track: false);
        if (active.Count == 0)
            return false;
        var budgets = await _repo.ListAsync<Budget>(null, ct, track: false);
        foreach (var budget in budgets)
        {
            var lines = await _repo.ListAsync<BudgetLine>(
                l => l.BudgetId == budget.Id && l.PlannedAmount != 0, ct, track: false);
            var filledIds = lines.Select(l => l.CategoryId).ToHashSet();
            if (active.Count(c => filledIds.Contains(c.Id)) * 2 >= active.Count)
                return true;
        }
        return false;
    }

    private async Task<string?> FirstPaymentOnTimeYmAsync(CancellationToken ct)
    {
        var locks = (await _repo.ListAsync<MonthLock>(m => m.IsClosed, ct, track: false))
            .OrderBy(m => m.CompetenceYm)
            .ToList();
        foreach (var month in locks)
        {
            var year = int.Parse(month.CompetenceYm.AsSpan(0, 4));
            var monthNum = int.Parse(month.CompetenceYm.AsSpan(5, 2));
            var budget = await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == month.CompetenceYm, ct, track: false);
            if (budget is null)
                continue;
            var lines = await _repo.ListAsync<BudgetLine>(
                l => l.BudgetId == budget.Id && l.ExpenseDay != null, ct, track: false);
            if (lines.Count == 0)
                continue;
            var ok = true;
            foreach (var line in lines)
            {
                var day = line.ExpenseDay!.Value;
                var entries = await _repo.ListAsync<Entry>(
                    e => e.CategoryId == line.CategoryId
                         && e.Type == EntryType.Expense
                         && e.OccurredAt.Year == year
                         && e.OccurredAt.Month == monthNum,
                    ct,
                    track: false);
                if (entries.Count == 0 || entries.Any(e => e.OccurredAt.Day > day))
                {
                    ok = false;
                    break;
                }
            }
            if (ok)
                return month.CompetenceYm;
        }
        return null;
    }

    private async Task<int> LongestWithinBudgetStreakAsync(IReadOnlyList<MonthLock> closed, CancellationToken ct)
    {
        if (closed.Count == 0)
            return 0;
        var ordered = closed.OrderBy(m => m.CompetenceYm).ToList();
        var best = 0;
        var current = 0;
        string? previous = null;
        foreach (var row in ordered)
        {
            var consecutive = previous is null || Competence.AddMonths(previous, 1) == row.CompetenceYm;
            if (!consecutive)
                current = 0;
            if (await IsWithinBudgetAsync(row.CompetenceYm, ct))
            {
                current++;
                best = Math.Max(best, current);
            }
            else
            {
                current = 0;
            }
            previous = row.CompetenceYm;
        }
        return best;
    }

    private async Task<bool> IsWithinBudgetAsync(string ym, CancellationToken ct)
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

    private async Task<BudgetCloseStatusResponse> BuildBudgetCloseAsync(string ym, CancellationToken ct)
    {
        var lockRow = await _repo.FirstOrDefaultAsync<MonthLock>(m => m.CompetenceYm == ym, ct, track: false);
        var budget = await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == ym, ct, track: false);
        decimal planned = 0;
        decimal actual = 0;
        if (budget is not null)
        {
            var lines = await _repo.ListAsync<BudgetLine>(l => l.BudgetId == budget.Id, ct);
            planned = lines.Sum(l => Math.Abs(l.PlannedAmount));
            var actuals = await _budgets.GetActualsAsync(ym, ct);
            actual = actuals.Sum(kv => Math.Abs(kv.Value));
        }

        return new BudgetCloseStatusResponse(
            ym,
            lockRow?.IsClosed ?? false,
            budget is not null,
            planned,
            actual,
            planned > 0 && actual <= planned);
    }

    private async Task<IReadOnlyList<ProjectProgressResponse>> BuildProjectProgressAsync(CancellationToken ct)
    {
        var list = await _projects.ListAsync(ct);
        return list
            .OrderByDescending(p => p.ProgressPercent)
            .Take(5)
            .Select(p =>
            {
                DateTime? eta = null;
                if (p.AccumulatedAmount > 0 && p.AccumulatedAmount < p.GoalAmount)
                {
                    var months = Competence.RangeInclusive(p.ContributionStartYm, Competence.From(p.DueDate)).Count;
                    if (months > 0)
                    {
                        var parcel = p.GoalAmount / months;
                        if (parcel > 0)
                        {
                            var remaining = p.GoalAmount - p.AccumulatedAmount;
                            var monthsLeft = (int)Math.Ceiling((double)(remaining / parcel));
                            eta = DateTime.UtcNow.Date.AddMonths(Math.Max(0, monthsLeft));
                        }
                    }
                }
                else if (p.AccumulatedAmount >= p.GoalAmount)
                {
                    eta = DateTime.UtcNow.Date;
                }

                return new ProjectProgressResponse(
                    p.Id,
                    p.Name,
                    p.Scope.ToString(),
                    p.GoalAmount,
                    p.AccumulatedAmount,
                    p.ProgressPercent,
                    eta,
                    p.Horizon);
            })
            .ToList();
    }

    private async Task<ReserveThermometerResponse> BuildReserveThermometerAsync(
        string ym,
        int targetMonths,
        CancellationToken ct)
    {
        var baseReserve = await _patrimony.GetReserveAsync(ym, ct, targetMonths);
        return new ReserveThermometerResponse(
            baseReserve.ReferenceYm,
            baseReserve.MonthlyEssentialAverage,
            baseReserve.MonthsConsidered,
            baseReserve.Source,
            baseReserve.ReserveBalance,
            baseReserve.MonthsCovered,
            baseReserve.TargetMonths,
            baseReserve.ProgressPercent);
    }

    private async Task<bool> HasReserveProgressAsync(CancellationToken ct)
    {
        var reserve = await _patrimony.GetReserveAsync(null, ct);
        return reserve.ReserveBalance > 0;
    }

    private async Task<User> RequireUserAsync(CancellationToken ct)
    {
        var id = _tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");
        return await _family.FindUserByIdAsync(id, ct)
               ?? throw new UserNotFoundException(id);
    }
}
