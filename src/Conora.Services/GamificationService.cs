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
        var hasDiagnosis = await _repo.AnyAsync<IncomeSource>(_ => true, ct);
        var hasBudget = await _repo.AnyAsync<Budget>(_ => true, ct);
        var hasCard = await _repo.AnyAsync<CreditCard>(_ => true, ct);
        var hasClosed = await _repo.AnyAsync<MonthLock>(m => m.IsClosed, ct);
        var hasReserve = await HasReserveProgressAsync(ct);
        var hasProject = await _repo.AnyAsync<LifeProject>(_ => true, ct);

        var defs = new (string Key, string Title, string Route, bool Done)[]
        {
            ("diagnosis", "Diagnóstico", "/diagnostico", hasDiagnosis),
            ("budget", "Orçamento", "/orcamento", hasBudget),
            ("card", "Cartão", "/cartoes", hasCard),
            ("close", "Fechamento", "/mes", hasClosed),
            ("reserve", "Reserva", "/projetos", hasReserve),
            ("projects", "Projetos de vida", "/projetos", hasProject),
        };

        var nextKey = defs.FirstOrDefault(d => !d.Done).Key;
        return defs.Select(d => new JourneyStepResponse(d.Key, d.Title, d.Route, d.Done, d.Key == nextKey && !d.Done)).ToList();
    }

    private async Task<IReadOnlyList<AchievementResponse>> BuildAchievementsAsync(CancellationToken ct)
    {
        var closedLocks = (await _repo.ListAsync<MonthLock>(m => m.IsClosed, ct))
            .OrderBy(m => m.CompetenceYm)
            .ToList();
        var firstClosed = closedLocks.FirstOrDefault();
        var firstClosedAt = firstClosed?.ClosedAt;

        var streak = await LongestWithinBudgetStreakAsync(closedLocks, ct);
        var threeInARow = streak >= 3;
        var threeAt = threeInARow
            ? closedLocks.Select(m => m.ClosedAt).Where(a => a is not null).OrderBy(a => a).Skip(2).FirstOrDefault()
            : null;

        var anyWithin = false;
        DateTime? withinAt = null;
        foreach (var row in closedLocks)
        {
            if (!await IsWithinBudgetAsync(row.CompetenceYm, ct))
                continue;
            anyWithin = true;
            withinAt = row.ClosedAt;
            break;
        }

        var reserve = await BuildReserveThermometerAsync(Competence.From(DateTime.UtcNow), (await RequireUserAsync(ct)).ReserveTargetMonths, ct);
        var reserveComplete = reserve.MonthsCovered >= reserve.TargetMonths;
        var reserveAt = reserveComplete ? DateTime.UtcNow : (DateTime?)null;

        var projects = await _repo.ListAsync<LifeProject>(null, ct);
        var advanced = projects.FirstOrDefault(p => p.AccumulatedAmount > 0);
        var advancedAt = advanced is null ? (DateTime?)null : advanced.UpdatedAt;

        return
        [
            new("first-month-closed", "1º mês fechado", "Você fechou o primeiro mês no Conora.", firstClosed is not null, firstClosedAt),
            new("three-months-in-budget", "3 meses seguidos", "Três competências fechadas dentro do orçamento.", threeInARow, threeAt),
            new("budget-in-order", "Orçamento em ordem", "Um mês fechado dentro do previsto.", anyWithin, withinAt),
            new("reserve-complete", "Reserva completa", "Reserva cobre a meta de meses de essenciais.", reserveComplete, reserveAt),
            new("project-progress", "Avanço em projeto de vida", "Primeiro aporte que move o acumulado de um projeto.", advanced is not null, advancedAt),
        ];
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
