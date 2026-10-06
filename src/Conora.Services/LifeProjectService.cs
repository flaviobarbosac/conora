using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class LifeProjectService
{
    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly PlanService _plan;
    private readonly EntryService _entries;

    public LifeProjectService(IFinanceRepository repo, IUnitOfWork uow, PlanService plan, EntryService entries)
    {
        _repo = repo;
        _uow = uow;
        _plan = plan;
        _entries = entries;
    }

    public async Task<IReadOnlyList<LifeProjectResponse>> ListAsync(CancellationToken ct)
    {
        var items = await _repo.ListAsync<LifeProject>(null, ct);
        return items.OrderBy(p => p.DueDate ?? DateTime.MaxValue).ThenBy(p => p.Name).Select(ToResponse).ToList();
    }

    public async Task<LifeProjectResponse> GetAsync(Guid id, CancellationToken ct)
        => ToResponse(await RequireAsync(id, ct, track: false));

    public async Task<LifeProjectResponse> CreateAsync(LifeProjectRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var project = LifeProject.Create(request.Name, request.GoalAmount, request.DueDate);
        _repo.Add(project);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(project);
    }

    public async Task<LifeProjectResponse> UpdateAsync(Guid id, LifeProjectRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var project = await RequireAsync(id, ct);
        project.Update(request.Name, request.GoalAmount, request.DueDate);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(project);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var project = await RequireAsync(id, ct);
        if (await _repo.AnyAsync<Entry>(e => e.LifeProjectId == id, ct))
            throw new ValidationException("id", "Projeto com aportes não pode ser excluído.");

        _repo.SoftDelete(project);
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>A contribution is an entry of type ProjectContribution; the entry service updates the accumulated amount.</summary>
    public async Task<LifeProjectResponse> ContributeAsync(Guid id, ProjectContributionRequest request, CancellationToken ct)
    {
        await RequireAsync(id, ct, track: false);
        await _entries.CreateAsync(new CreateEntryRequest(
            EntryType.ProjectContribution,
            request.Amount,
            request.OccurredAt,
            string.IsNullOrWhiteSpace(request.Description) ? "Aporte em projeto de vida" : request.Description,
            AccountId: request.AccountId,
            LifeProjectId: id), ct);

        return await GetAsync(id, ct);
    }

    private async Task<LifeProject> RequireAsync(Guid id, CancellationToken ct, bool track = true)
        => await _repo.FirstOrDefaultAsync<LifeProject>(p => p.Id == id, ct, track) ?? throw new NotFoundException("Projeto de vida", id);

    private static LifeProjectResponse ToResponse(LifeProject p) => new(
        p.Id,
        p.Name,
        p.GoalAmount,
        p.DueDate,
        p.AccumulatedAmount,
        p.GoalAmount <= 0 ? 0 : Math.Min(100m, decimal.Round(p.AccumulatedAmount / p.GoalAmount * 100, 1)));
}
