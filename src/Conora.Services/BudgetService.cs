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

    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly PlanService _plan;
    private readonly MonthService _months;
    private readonly CategoryService _categories;

    public BudgetService(
        IFinanceRepository repo, IUnitOfWork uow, PlanService plan, MonthService months, CategoryService categories)
    {
        _repo = repo;
        _uow = uow;
        _plan = plan;
        _months = months;
        _categories = categories;
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

        var entries = await _repo.QueryAsync<Entry, CategorySum>(q => q
            .Where(e => e.CompetenceYm == ym
                        && (e.Type == EntryType.Expense || e.Type == EntryType.Contribution)
                        && e.CategoryId != null)
            .GroupBy(e => e.CategoryId!.Value)
            .Select(g => new CategorySum(g.Key, g.Sum(x => x.Amount))), ct);

        var purchases = await _repo.QueryAsync<CardPurchase, CategorySum>(q => q
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
        var budget = await _repo.FirstOrDefaultAsync<Budget>(b => b.CompetenceYm == ym, ct, track: false);
        var lines = budget is null
            ? []
            : await _repo.ListAsync<BudgetLine>(l => l.BudgetId == budget.Id, ct);

        var actuals = await GetActualsAsync(ym, ct);
        var categories = (await _categories.ListAsync(null, true, ct)).ToDictionary(c => c.Id, c => c.Name);

        var responses = lines
            .Select(l =>
            {
                var actual = actuals.GetValueOrDefault(l.CategoryId);
                var (percent, status) = Evaluate(l.PlannedAmount, actual);
                return new BudgetLineResponse(
                    l.CategoryId,
                    categories.GetValueOrDefault(l.CategoryId, "—"),
                    l.PlannedAmount,
                    actual,
                    l.PlannedAmount - actual,
                    percent,
                    status);
            })
            .OrderBy(l => l.CategoryName)
            .ToList();

        var totalActual = actuals.Values.Sum();
        return new BudgetResponse(
            ym,
            budget?.Mode ?? BudgetMode.Simple,
            responses.Sum(l => l.PlannedAmount),
            totalActual,
            Project(ym, totalActual),
            responses);
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

            await SetLineAsync(budget, input.CategoryId, input.PlannedAmount, ct);
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
        await SetLineAsync(budget, contributions.Id, Math.Max(0, totalTithe), ct);
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
