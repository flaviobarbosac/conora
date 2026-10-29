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

        var fullAccess = await _plan.HasFullAccessAsync(ct);
        var accessPolicy = _categoryAccess.ForFullAccess(fullAccess);
        foreach (var input in request.Lines.GroupBy(l => l.CategoryId).Select(g => g.Last()))
        {
            var account = await _categories.RequireAnalyticalAsync(input.CategoryId, ct);
            if (!BudgetSections.Contains(account.Section))
                throw new ValidationException("lines", $"A conta '{account.Name}' não aceita orçamento (somente receita e despesa).");
            if (!accessPolicy.CanWriteAnalytical(account.Code, account.Section))
                await _plan.EnsureCategoryWritableAsync(account.Code, ct);
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
}
