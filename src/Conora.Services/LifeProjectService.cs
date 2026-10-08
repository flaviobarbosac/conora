using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class LifeProjectService
{
    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly PlanService _plan;
    private readonly EntryService _entries;
    private readonly FamilyGroupService _family;
    private readonly ChartAccountService _chartAccounts;
    private readonly BudgetService _budgets;
    private readonly ITenantContext _tenant;

    public LifeProjectService(
        IFinanceRepository repo,
        IUnitOfWork uow,
        PlanService plan,
        EntryService entries,
        FamilyGroupService family,
        ChartAccountService chartAccounts,
        BudgetService budgets,
        ITenantContext tenant)
    {
        _repo = repo;
        _uow = uow;
        _plan = plan;
        _entries = entries;
        _family = family;
        _chartAccounts = chartAccounts;
        _budgets = budgets;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<LifeProjectResponse>> ListAsync(CancellationToken ct)
    {
        var self = _tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");
        var own = await _repo.ListAsync<LifeProject>(null, ct);
        var peers = await _family.GetReadableUsuarioIdsAsync(ct);
        var groupProjects = peers.Count > 1
            ? await _repo.ListAnyTenantAsync<LifeProject>(
                p => peers.Contains(p.UsuarioId) && p.UsuarioId != self && p.Scope == LifeProjectScope.Group, ct)
            : [];

        var accounts = (await _chartAccounts.ListAsync(ChartSection.LifeProject, true, false, ct))
            .ToDictionary(c => c.Id);
        return own.Concat(groupProjects)
            .OrderBy(p => p.DueDate)
            .ThenBy(p => p.Name)
            .Select(p => ToResponse(p, self, accounts))
            .ToList();
    }

    public async Task<LifeProjectResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var self = _tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");
        var project = await RequireVisibleAsync(id, ct, track: false);
        var accounts = (await _chartAccounts.ListAsync(ChartSection.LifeProject, true, false, ct))
            .ToDictionary(c => c.Id);
        return ToResponse(project, self, accounts);
    }

    public async Task<LifeProjectResponse> CreateAsync(LifeProjectRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        if (request.Scope == LifeProjectScope.Group)
        {
            var peers = await _family.GetReadableUsuarioIdsAsync(ct);
            if (peers.Count < 2)
                throw new ValidationException("scope", "Sem grupo ativo não dá para marcar o projeto como do grupo.");
        }

        var chartAccountId = await RequireLifeProjectAccountAsync(request.ChartAccountId, ct);
        await EnsureAccountFreeAsync(chartAccountId, null, ct);
        EnsureContributionWindow(request.ContributionStartYm, request.DueDate, previousStartYm: null);
        var project = LifeProject.Create(
            request.Name,
            decimal.Round(request.GoalAmount, 2),
            request.DueDate,
            request.ContributionStartYm,
            chartAccountId,
            request.Scope,
            request.DetailedDescription);
        _repo.Add(project);
        await _uow.SaveChangesAsync(ct);
        await SyncBudgetLinesAsync(project, previousAccountId: null, previousMonths: null, ct);
        return await GetAsync(project.Id, ct);
    }

    public async Task<LifeProjectResponse> UpdateAsync(Guid id, LifeProjectRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var self = _tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");
        var project = await _repo.FirstOrDefaultAsync<LifeProject>(p => p.Id == id, ct)
                      ?? throw new NotFoundException("Projeto de vida", id);
        if (project.UsuarioId != self)
            throw new ForbiddenException("Só o dono pode editar o projeto.");

        if (request.Scope == LifeProjectScope.Group)
        {
            var peers = await _family.GetReadableUsuarioIdsAsync(ct);
            if (peers.Count < 2)
                throw new ValidationException("scope", "Sem grupo ativo não dá para marcar o projeto como do grupo.");
        }

        var chartAccountId = await RequireLifeProjectAccountAsync(request.ChartAccountId, ct);
        await EnsureAccountFreeAsync(chartAccountId, project.Id, ct);
        EnsureContributionWindow(request.ContributionStartYm, request.DueDate, project.ContributionStartYm);
        var previousAccountId = project.ChartAccountId;
        var previousMonths = MonthsOf(project);
        project.Update(
            request.Name,
            decimal.Round(request.GoalAmount, 2),
            request.DueDate,
            request.ContributionStartYm,
            chartAccountId,
            request.Scope,
            request.DetailedDescription);
        await _uow.SaveChangesAsync(ct);
        await SyncBudgetLinesAsync(project, previousAccountId, previousMonths, ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var self = _tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");
        var project = await _repo.FirstOrDefaultAsync<LifeProject>(p => p.Id == id, ct)
                      ?? throw new NotFoundException("Projeto de vida", id);
        if (project.UsuarioId != self)
            throw new ForbiddenException("Só o dono pode excluir o projeto.");
        if (await _repo.AnyAsync<Entry>(e => e.LifeProjectId == id, ct))
            throw new ValidationException("id", "Projeto com aportes não pode ser excluído.");

        if (project.ChartAccountId is Guid accountId)
            await _budgets.ClearPlannedForAccountAsync(accountId, MonthsOf(project), ct);

        _repo.SoftDelete(project);
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>A contribution is an entry of type ProjectContribution; the entry service updates the accumulated amount.</summary>
    public async Task<LifeProjectResponse> ContributeAsync(Guid id, ProjectContributionRequest request, CancellationToken ct)
    {
        var project = await RequireVisibleAsync(id, ct, track: false);
        if (project.ChartAccountId is null)
            throw new ValidationException("chartAccountId", "Vincule o projeto a uma conta do plano antes de aportar.");

        await _entries.CreateAsync(new CreateEntryRequest(
            EntryType.ProjectContribution,
            decimal.Round(request.Amount, 2),
            request.OccurredAt,
            string.IsNullOrWhiteSpace(request.Description) ? "Aporte em projeto de vida" : request.Description,
            AccountId: request.AccountId,
            ChartAccountId: project.ChartAccountId,
            LifeProjectId: id), ct);

        return await GetAsync(id, ct);
    }

    private async Task<Guid> RequireLifeProjectAccountAsync(Guid? chartAccountId, CancellationToken ct)
    {
        if (chartAccountId is null)
            throw new ValidationException("chartAccountId", "Escolha a conta do plano de contas para o projeto.");

        var account = await _chartAccounts.RequireAnalyticalAsync(chartAccountId.Value, ct);
        if (account.Section != ChartSection.LifeProject)
            throw new ValidationException("chartAccountId", "O projeto só aceita contas da seção Projetos de vida.");

        return account.Id;
    }

    private async Task<LifeProject> RequireVisibleAsync(Guid id, CancellationToken ct, bool track = true)
    {
        var self = _tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");
        var own = await _repo.FirstOrDefaultAsync<LifeProject>(p => p.Id == id, ct, track);
        if (own is not null)
            return own;

        var project = await _repo.FirstOrDefaultAnyTenantAsync<LifeProject>(p => p.Id == id, ct)
                      ?? throw new NotFoundException("Projeto de vida", id);
        if (project.Scope != LifeProjectScope.Group || !await _family.IsInSameGroupAsync(project.UsuarioId, ct))
            throw new NotFoundException("Projeto de vida", id);
        return project;
    }

    private static LifeProjectResponse ToResponse(
        LifeProject p,
        Guid self,
        IReadOnlyDictionary<Guid, ChartAccountResponse> accounts)
    {
        string? accountName = null;
        if (p.ChartAccountId is Guid aid && accounts.TryGetValue(aid, out var account))
            accountName = string.IsNullOrWhiteSpace(account.DisplayNumber)
                ? account.Name
                : $"{account.DisplayNumber} {account.Name}";

        return new(
            p.Id,
            p.Name,
            p.DetailedDescription,
            p.GoalAmount,
            p.DueDate,
            p.ContributionStartYm,
            p.AccumulatedAmount,
            p.GoalAmount <= 0 ? 0 : Math.Min(100m, decimal.Round(p.AccumulatedAmount / p.GoalAmount * 100, 1)),
            p.Scope,
            p.UsuarioId == self,
            p.ChartAccountId,
            accountName,
            ResolveHorizon(p.ChartAccountId, accounts));
    }

    private static string? ResolveHorizon(Guid? chartAccountId, IReadOnlyDictionary<Guid, ChartAccountResponse> accounts)
    {
        if (chartAccountId is not Guid id)
            return null;

        ChartAccountResponse? current = accounts.GetValueOrDefault(id);
        while (current is not null)
        {
            if (current.Code is "LIFE_SHORT" or "LIFE_MID" or "LIFE_LONG")
            {
                return current.Code switch
                {
                    "LIFE_SHORT" => "short",
                    "LIFE_MID" => "mid",
                    "LIFE_LONG" => "long",
                    _ => null
                };
            }

            current = current.ParentId is Guid parentId ? accounts.GetValueOrDefault(parentId) : null;
        }

        return null;
    }

    private async Task EnsureAccountFreeAsync(Guid chartAccountId, Guid? ignoreProjectId, CancellationToken ct)
    {
        var clash = await _repo.AnyAsync<LifeProject>(
            p => p.ChartAccountId == chartAccountId && p.Id != ignoreProjectId, ct);
        if (clash)
            throw new ValidationException("chartAccountId", "Já existe um projeto nesta conta do plano.");
    }

    private static void EnsureContributionWindow(string contributionStartYm, DateTime dueDate, string? previousStartYm)
    {
        var startYm = Competence.Require(contributionStartYm, "contributionStartYm");
        var currentYm = Competence.From(DateTime.UtcNow);
        if (Competence.Compare(startYm, currentYm) < 0 && startYm != previousStartYm)
            throw new ValidationException("contributionStartYm", "O início do aporte não pode ser antes do mês atual.");

        Competence.RangeInclusive(startYm, Competence.From(dueDate));
    }

    private static IReadOnlyList<string> MonthsOf(LifeProject project)
        => Competence.RangeInclusive(project.ContributionStartYm, Competence.From(project.DueDate));

    private async Task SyncBudgetLinesAsync(
        LifeProject project,
        Guid? previousAccountId,
        IReadOnlyList<string>? previousMonths,
        CancellationToken ct)
    {
        if (project.ChartAccountId is not Guid accountId)
            return;

        if (previousAccountId is Guid oldAccount && oldAccount != accountId && previousMonths is { Count: > 0 })
            await _budgets.ClearPlannedForAccountAsync(oldAccount, previousMonths, ct);
        else if (previousMonths is { Count: > 0 })
        {
            var next = MonthsOf(project);
            var removed = previousMonths.Except(next).ToList();
            if (removed.Count > 0)
                await _budgets.ClearPlannedForAccountAsync(accountId, removed, ct);
        }

        var months = MonthsOf(project);
        var planned = decimal.Round(project.GoalAmount / months.Count, 2);
        await _budgets.UpsertPlannedForAccountAsync(accountId, months, planned, ct);
    }
}
