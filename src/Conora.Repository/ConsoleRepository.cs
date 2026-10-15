using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Services;
using Conora.Infrastructure.Persistence;
using Conora.Repository.Interface;
using Microsoft.EntityFrameworkCore;

namespace Conora.Repository;

public sealed class ConsoleRepository : IConsoleRepository
{
    private static readonly EntryType[] SpentTypes =
    [
        EntryType.Expense,
        EntryType.Contribution,
        EntryType.ProjectContribution
    ];

    private static readonly CategorySection[] SpentSections =
    [
        CategorySection.Essential,
        CategorySection.Social,
        CategorySection.LifeProject,
        CategorySection.Discount
    ];

    private readonly AppDbContext _db;

    public ConsoleRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ConsoleReadModel> ReadAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var competence = Competence.From(now);
        var previous = Competence.AddMonths(competence, -1);
        var weekStart = StartOfWeek(now);
        var sinceWeeks = weekStart.AddDays(-7 * 11);
        var since30 = now.AddDays(-30);
        var since7 = now.AddDays(-7);
        var since1 = now.AddDays(-1);

        var users = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.DeletedAt == null)
            .Select(u => new UserRow(u.Id, u.CreatedAt, u.OnboardingCompleted, u.ReserveTargetMonths))
            .ToListAsync(ct);
        var active = users.Select(u => u.Id).ToHashSet();

        var diagnosis = await DistinctAsync(_db.IncomeSources.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.DeletedAt == null).Select(s => s.UsuarioId), active, ct);
        var budgetEver = await DistinctAsync(_db.Budgets.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.DeletedAt == null).Select(b => b.UsuarioId), active, ct);
        var cards = await DistinctAsync(_db.CreditCards.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.DeletedAt == null).Select(c => c.UsuarioId), active, ct);
        var closedEver = await _db.MonthLocks.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.DeletedAt == null && m.IsClosed)
            .Select(m => new MonthRow(m.UsuarioId, m.CompetenceYm))
            .ToListAsync(ct);
        closedEver = closedEver.Where(m => active.Contains(m.UsuarioId)).ToList();

        var projects = await _db.LifeProjects.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.DeletedAt == null)
            .Select(p => new ProjectRow(p.UsuarioId, p.CategoryId, p.AccumulatedAmount))
            .ToListAsync(ct);
        projects = projects.Where(p => active.Contains(p.UsuarioId)).ToList();

        var reserveCategoryIds = await _db.Categories.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.DeletedAt == null && c.Code == "LIFE_RESERVE")
            .Select(c => c.Id)
            .ToListAsync(ct);

        var reserveContributions = reserveCategoryIds.Count == 0
            ? []
            : await _db.Entries.IgnoreQueryFilters().AsNoTracking()
                .Where(e => e.DeletedAt == null
                            && e.Type == EntryType.Contribution
                            && e.CategoryId != null
                            && reserveCategoryIds.Contains(e.CategoryId.Value))
                .GroupBy(e => e.UsuarioId)
                .Select(g => new AmountRow(g.Key, g.Sum(x => x.Amount)))
                .ToListAsync(ct);

        var budgetUsers = await DistinctAsync(_db.Budgets.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.DeletedAt == null && b.CompetenceYm == competence)
            .Select(b => b.UsuarioId), active, ct);
        var previousEntries = await DistinctAsync(_db.Entries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.DeletedAt == null && e.CompetenceYm == previous)
            .Select(e => e.UsuarioId), active, ct);
        var previousClosed = await DistinctAsync(_db.MonthLocks.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.DeletedAt == null && m.IsClosed && m.CompetenceYm == previous)
            .Select(m => m.UsuarioId), active, ct);

        var competences = closedEver.Select(m => m.CompetenceYm).Append(competence).Append(previous).Distinct().ToList();
        var cells = await AdherenceAsync(competences, active, ct);

        var withinEver = closedEver
            .Where(m => cells.TryGetValue((m.UsuarioId, m.CompetenceYm), out var cell) && MethodAdherence.IsWithin(cell.Planned, cell.Spent))
            .Select(m => m.UsuarioId)
            .Distinct()
            .Count();
        var streak = MethodAdherence.CountUsersWithStreak(
            closedEver.Select(m => (m.UsuarioId, m.CompetenceYm)).ToList(),
            cells);

        var (within, over, empty) = SplitBudget(budgetUsers, competence, cells);
        var withinPrevious = previousClosed.Count(id =>
            cells.TryGetValue((id, previous), out var cell) && MethodAdherence.IsWithin(cell.Planned, cell.Spent));

        var essentialPlanned = await EssentialPlannedAsync(competence, active, ct);
        var (withReserve, reserveComplete) = Reserve(users, projects, reserveContributions, reserveCategoryIds, essentialPlanned);

        var hits = await HitsAsync(sinceWeeks, active, ct);
        var features = FeatureCounts(hits, since30);
        var active1 = ActiveCount(hits, since1);
        var active7 = ActiveCount(hits, since7);
        var active30 = ActiveCount(hits, since30);

        var budgetCreated = await _db.Budgets.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.DeletedAt == null && b.CreatedAt >= sinceWeeks)
            .Select(b => new WhenRow(b.UsuarioId, b.CreatedAt))
            .ToListAsync(ct);
        var closes = await _db.MonthLocks.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.DeletedAt == null && m.ClosedAt != null && m.ClosedAt >= sinceWeeks)
            .Select(m => new WhenRow(m.UsuarioId, m.ClosedAt!.Value))
            .ToListAsync(ct);

        var failures = await _db.RequestFailures.AsNoTracking()
            .Where(f => f.DeletedAt == null && f.OccurredAt >= sinceWeeks)
            .Select(f => new FailureRow(f.StatusCode, f.Title, f.Area, f.OccurredAt))
            .ToListAsync(ct);

        var weeks = Weeks(weekStart, users, hits, budgetCreated.Where(r => active.Contains(r.UsuarioId)), closes.Where(r => active.Contains(r.UsuarioId)), failures);
        var recent = failures.Where(f => f.OccurredAt >= since30).ToList();

        return new ConsoleReadModel(
            now,
            competence,
            previous,
            users.Count,
            users.Count(u => u.OnboardingCompleted),
            diagnosis.Count,
            budgetEver.Count,
            cards.Count,
            closedEver.Select(m => m.UsuarioId).Distinct().Count(),
            withReserve,
            reserveComplete,
            projects.Select(p => p.UsuarioId).Distinct().Count(),
            projects.Where(p => p.AccumulatedAmount > 0).Select(p => p.UsuarioId).Distinct().Count(),
            withinEver,
            streak,
            budgetUsers.Count,
            within,
            over,
            empty,
            previousEntries.Count,
            previousClosed.Count,
            withinPrevious,
            active1,
            active7,
            active30,
            failures.Count(f => f.OccurredAt >= since7),
            recent.Count,
            features,
            weeks,
            ErrorDays(recent, now),
            recent.GroupBy(f => (f.StatusCode, f.Title))
                .Select(g => new ConsoleStatusRaw(g.Key.StatusCode, g.Key.Title, g.Count()))
                .OrderByDescending(g => g.Count)
                .ToList(),
            recent.GroupBy(f => f.Area)
                .Select(g => new ConsoleAreaRaw(g.Key, g.Count()))
                .OrderByDescending(g => g.Count)
                .ToList(),
            recent.GroupBy(f => (Day: DateOnly.FromDateTime(f.OccurredAt), f.StatusCode, f.Title, f.Area))
                .Select(g => new ConsoleErrorRowRaw(g.Key.Day, g.Key.StatusCode, g.Key.Title, g.Key.Area, g.Count()))
                .OrderByDescending(g => g.Day)
                .ThenByDescending(g => g.Count)
                .Take(40)
                .ToList());
    }

    private async Task<Dictionary<(Guid UserId, string Ym), (decimal Planned, decimal Spent)>> AdherenceAsync(
        IReadOnlyCollection<string> competences,
        HashSet<Guid> active,
        CancellationToken ct)
    {
        var cells = new Dictionary<(Guid, string), (decimal Planned, decimal Spent)>();
        if (competences.Count == 0)
            return cells;

        var planned = await _db.BudgetLines.IgnoreQueryFilters().AsNoTracking()
            .Where(line => line.DeletedAt == null)
            .Join(
                _db.Budgets.Where(b => b.DeletedAt == null && competences.Contains(b.CompetenceYm)),
                line => line.BudgetId,
                budget => budget.Id,
                (line, budget) => new { budget.UsuarioId, budget.CompetenceYm, line.PlannedAmount })
            .GroupBy(row => new { row.UsuarioId, row.CompetenceYm })
            .Select(g => new CellRow(g.Key.UsuarioId, g.Key.CompetenceYm, g.Sum(x => x.PlannedAmount)))
            .ToListAsync(ct);

        var spent = await _db.Entries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.DeletedAt == null
                        && e.CategoryId != null
                        && competences.Contains(e.CompetenceYm)
                        && SpentTypes.Contains(e.Type))
            .Join(
                _db.Categories.Where(c => c.DeletedAt == null && SpentSections.Contains(c.Section)),
                entry => entry.CategoryId,
                category => (Guid?)category.Id,
                (entry, _) => new { entry.UsuarioId, entry.CompetenceYm, entry.Amount })
            .GroupBy(row => new { row.UsuarioId, row.CompetenceYm })
            .Select(g => new CellRow(g.Key.UsuarioId, g.Key.CompetenceYm, g.Sum(x => x.Amount)))
            .ToListAsync(ct);

        foreach (var row in planned.Where(row => active.Contains(row.UsuarioId)))
            cells[(row.UsuarioId, row.CompetenceYm)] = (row.Amount, 0m);
        foreach (var row in spent.Where(row => active.Contains(row.UsuarioId)))
        {
            var key = (row.UsuarioId, row.CompetenceYm);
            var plannedAmount = cells.TryGetValue(key, out var cell) ? cell.Planned : 0m;
            cells[key] = (plannedAmount, row.Amount);
        }

        return cells;
    }

    private async Task<List<Hit>> HitsAsync(DateTime since, HashSet<Guid> active, CancellationToken ct)
    {
        var hits = new List<Hit>();
        hits.AddRange(await FeatureAsync("entries", _db.Entries.IgnoreQueryFilters().AsNoTracking().Where(e => e.DeletedAt == null && e.UpdatedAt >= since).Select(e => new WhenRow(e.UsuarioId, e.UpdatedAt)), active, ct));
        hits.AddRange(await FeatureAsync("budgets", _db.Budgets.IgnoreQueryFilters().AsNoTracking().Where(b => b.DeletedAt == null && b.UpdatedAt >= since).Select(b => new WhenRow(b.UsuarioId, b.UpdatedAt)), active, ct));
        hits.AddRange(await FeatureAsync("months", _db.MonthLocks.IgnoreQueryFilters().AsNoTracking().Where(m => m.DeletedAt == null && m.UpdatedAt >= since).Select(m => new WhenRow(m.UsuarioId, m.UpdatedAt)), active, ct));
        hits.AddRange(await FeatureAsync("diagnosis", _db.IncomeSources.IgnoreQueryFilters().AsNoTracking().Where(s => s.DeletedAt == null && s.UpdatedAt >= since).Select(s => new WhenRow(s.UsuarioId, s.UpdatedAt)), active, ct));
        hits.AddRange(await FeatureAsync("accounts", _db.Accounts.IgnoreQueryFilters().AsNoTracking().Where(a => a.DeletedAt == null && a.UpdatedAt >= since).Select(a => new WhenRow(a.UsuarioId, a.UpdatedAt)), active, ct));
        hits.AddRange(await FeatureAsync("cards", _db.CreditCards.IgnoreQueryFilters().AsNoTracking().Where(c => c.DeletedAt == null && c.UpdatedAt >= since).Select(c => new WhenRow(c.UsuarioId, c.UpdatedAt)), active, ct));
        hits.AddRange(await FeatureAsync("cards", _db.CardPurchases.IgnoreQueryFilters().AsNoTracking().Where(c => c.DeletedAt == null && c.UpdatedAt >= since).Select(c => new WhenRow(c.UsuarioId, c.UpdatedAt)), active, ct));
        hits.AddRange(await FeatureAsync("categories", _db.Categories.IgnoreQueryFilters().AsNoTracking().Where(c => c.DeletedAt == null && c.UpdatedAt >= since && !c.IsSystem).Select(c => new WhenRow(c.UsuarioId, c.UpdatedAt)), active, ct));
        hits.AddRange(await FeatureAsync("projects", _db.LifeProjects.IgnoreQueryFilters().AsNoTracking().Where(p => p.DeletedAt == null && p.UpdatedAt >= since).Select(p => new WhenRow(p.UsuarioId, p.UpdatedAt)), active, ct));
        hits.AddRange(await FeatureAsync("patrimony", _db.PatrimonyItems.IgnoreQueryFilters().AsNoTracking().Where(p => p.DeletedAt == null && p.UpdatedAt >= since).Select(p => new WhenRow(p.UsuarioId, p.UpdatedAt)), active, ct));
        hits.AddRange(await FeatureAsync("imports", _db.ImportBatches.IgnoreQueryFilters().AsNoTracking().Where(b => b.DeletedAt == null && b.UpdatedAt >= since).Select(b => new WhenRow(b.UsuarioId, b.UpdatedAt)), active, ct));
        hits.AddRange(await FeatureAsync("receipts", _db.ReceiptIntakes.IgnoreQueryFilters().AsNoTracking().Where(r => r.DeletedAt == null && r.UpdatedAt >= since).Select(r => new WhenRow(r.UsuarioId, r.UpdatedAt)), active, ct));
        hits.AddRange(await FeatureAsync("whatsapp", _db.WhatsAppDrafts.IgnoreQueryFilters().AsNoTracking().Where(d => d.DeletedAt == null && d.UpdatedAt >= since).Select(d => new WhenRow(d.UsuarioId, d.UpdatedAt)), active, ct));
        hits.AddRange(await FeatureAsync("members", _db.FamilyMembers.IgnoreQueryFilters().AsNoTracking().Where(m => m.DeletedAt == null && m.UpdatedAt >= since).Select(m => new WhenRow(m.UsuarioId, m.UpdatedAt)), active, ct));

        var family = await _db.FamilyGroupMembers.AsNoTracking()
            .Where(m => m.DeletedAt == null && m.UpdatedAt >= since)
            .Select(m => new WhenRow(m.UsuarioId, m.UpdatedAt))
            .ToListAsync(ct);
        hits.AddRange(family.Where(row => active.Contains(row.UsuarioId)).Select(row => new Hit("family", row.UsuarioId, row.At)));

        var audits = await _db.AuditEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.DeletedAt == null
                        && a.TimestampUtc >= since
                        && (a.EntityName == "Ian" || a.EntityName == "Gemini" || a.EntityName == "UserFeedback"))
            .Select(a => new AuditHit(a.UsuarioId, a.TimestampUtc, a.EntityName))
            .ToListAsync(ct);
        foreach (var row in audits.Where(row => active.Contains(row.UsuarioId)))
        {
            var key = row.EntityName == "UserFeedback" ? "feedback" : "ian";
            hits.Add(new Hit(key, row.UsuarioId, row.At));
        }

        return hits;
    }

    private static async Task<List<Hit>> FeatureAsync(
        string key,
        IQueryable<WhenRow> query,
        HashSet<Guid> active,
        CancellationToken ct)
    {
        var rows = await query.ToListAsync(ct);
        return rows.Where(row => active.Contains(row.UsuarioId)).Select(row => new Hit(key, row.UsuarioId, row.At)).ToList();
    }

    private static async Task<HashSet<Guid>> DistinctAsync(IQueryable<Guid> ids, HashSet<Guid> active, CancellationToken ct)
    {
        var list = await ids.Distinct().ToListAsync(ct);
        return list.Where(active.Contains).ToHashSet();
    }

    private static (int Within, int Over, int Empty) SplitBudget(
        HashSet<Guid> budgetUsers,
        string competence,
        Dictionary<(Guid UserId, string Ym), (decimal Planned, decimal Spent)> cells)
    {
        var within = 0;
        var over = 0;
        var empty = 0;
        foreach (var userId in budgetUsers)
        {
            var planned = cells.TryGetValue((userId, competence), out var cell) ? cell.Planned : 0m;
            var spent = cell.Spent;
            if (planned <= 0m)
                empty++;
            else if (spent <= planned)
                within++;
            else
                over++;
        }

        return (within, over, empty);
    }

    private async Task<Dictionary<Guid, decimal>> EssentialPlannedAsync(string competence, HashSet<Guid> active, CancellationToken ct)
    {
        var rows = await (
            from line in _db.BudgetLines.IgnoreQueryFilters().AsNoTracking()
            join budget in _db.Budgets on line.BudgetId equals budget.Id
            join category in _db.Categories on line.CategoryId equals category.Id
            where line.DeletedAt == null
                  && budget.DeletedAt == null
                  && category.DeletedAt == null
                  && budget.CompetenceYm == competence
                  && category.Section == CategorySection.Essential
            group line by budget.UsuarioId
            into g
            select new AmountRow(g.Key, g.Sum(x => x.PlannedAmount))
        ).ToListAsync(ct);

        return rows.Where(row => active.Contains(row.UsuarioId)).ToDictionary(row => row.UsuarioId, row => row.Amount);
    }

    private static (int WithReserve, int Complete) Reserve(
        IReadOnlyList<UserRow> users,
        IReadOnlyList<ProjectRow> projects,
        IReadOnlyList<AmountRow> contributions,
        IReadOnlyCollection<Guid> reserveCategoryIds,
        IReadOnlyDictionary<Guid, decimal> essentialPlanned)
    {
        var reserveIds = reserveCategoryIds.ToHashSet();
        var projectSums = projects
            .Where(p => p.CategoryId is Guid id && reserveIds.Contains(id))
            .GroupBy(p => p.UsuarioId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.AccumulatedAmount));
        var entrySums = contributions.ToDictionary(row => row.UsuarioId, row => row.Amount);

        var withReserve = 0;
        var complete = 0;
        foreach (var user in users)
        {
            var balance = projectSums.TryGetValue(user.Id, out var fromProject)
                ? fromProject
                : entrySums.GetValueOrDefault(user.Id);
            if (balance <= 0m)
                continue;
            withReserve++;
            if (essentialPlanned.TryGetValue(user.Id, out var planned)
                && planned > 0m
                && balance / planned >= user.ReserveTargetMonths)
                complete++;
        }

        return (withReserve, complete);
    }

    private static List<ConsoleFeatureRaw> FeatureCounts(IReadOnlyList<Hit> hits, DateTime since)
    {
        return hits.Where(hit => hit.At >= since)
            .GroupBy(hit => hit.Feature)
            .Select(g => new ConsoleFeatureRaw(g.Key, g.Select(hit => hit.UserId).Distinct().Count(), g.Count()))
            .OrderByDescending(row => row.Users)
            .ThenByDescending(row => row.Events)
            .ToList();
    }

    private static int ActiveCount(IReadOnlyList<Hit> hits, DateTime since)
        => hits.Where(hit => hit.At >= since).Select(hit => hit.UserId).Distinct().Count();

    private static List<ConsoleWeekRaw> Weeks(
        DateTime currentWeek,
        IReadOnlyList<UserRow> users,
        IReadOnlyList<Hit> hits,
        IEnumerable<WhenRow> budgets,
        IEnumerable<WhenRow> closes,
        IReadOnlyList<FailureRow> failures)
    {
        var budgetList = budgets.ToList();
        var closeList = closes.ToList();
        var weeks = new List<ConsoleWeekRaw>(12);
        for (var index = 11; index >= 0; index--)
        {
            var start = currentWeek.AddDays(-7 * index);
            var end = start.AddDays(7);
            var day = DateOnly.FromDateTime(start);
            weeks.Add(new ConsoleWeekRaw(
                day,
                users.Count(u => u.CreatedAt >= start && u.CreatedAt < end),
                hits.Where(hit => hit.At >= start && hit.At < end).Select(hit => hit.UserId).Distinct().Count(),
                budgetList.Count(row => row.At >= start && row.At < end),
                closeList.Count(row => row.At >= start && row.At < end),
                failures.Count(row => row.OccurredAt >= start && row.OccurredAt < end)));
        }

        return weeks;
    }

    private static List<ConsoleDayRaw> ErrorDays(IReadOnlyList<FailureRow> failures, DateTime now)
    {
        var days = new List<ConsoleDayRaw>(14);
        for (var offset = 13; offset >= 0; offset--)
        {
            var day = DateOnly.FromDateTime(now.AddDays(-offset));
            days.Add(new ConsoleDayRaw(day, failures.Count(row => DateOnly.FromDateTime(row.OccurredAt) == day)));
        }

        return days;
    }

    private static DateTime StartOfWeek(DateTime utc)
    {
        var date = utc.Date;
        var diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-diff);
    }

    private sealed record UserRow(Guid Id, DateTime CreatedAt, bool OnboardingCompleted, int ReserveTargetMonths);
    private sealed record MonthRow(Guid UsuarioId, string CompetenceYm);
    private sealed record ProjectRow(Guid UsuarioId, Guid? CategoryId, decimal AccumulatedAmount);
    private sealed record AmountRow(Guid UsuarioId, decimal Amount);
    private sealed record CellRow(Guid UsuarioId, string CompetenceYm, decimal Amount);
    private sealed record WhenRow(Guid UsuarioId, DateTime At);
    private sealed record FailureRow(int StatusCode, string Title, string Area, DateTime OccurredAt);
    private sealed record AuditHit(Guid UsuarioId, DateTime At, string EntityName);
    private readonly record struct Hit(string Feature, Guid UserId, DateTime At);
}
