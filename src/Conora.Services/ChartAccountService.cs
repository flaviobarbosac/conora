using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class ChartAccountService
{
    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly PlanService _plan;

    public ChartAccountService(IFinanceRepository repo, IUnitOfWork uow, PlanService plan)
    {
        _repo = repo;
        _uow = uow;
        _plan = plan;
    }

    /// <summary>Adds the system chart of accounts without saving (used when a new user registers).</summary>
    public void AddDefaults()
    {
        var byCode = new Dictionary<string, ChartAccount>(StringComparer.Ordinal);
        foreach (var definition in SystemChartAccounts.All.OrderBy(d => d.SortOrder))
        {
            Guid? parentId = definition.ParentCode is null ? null : byCode[definition.ParentCode].Id;
            var account = ChartAccount.CreateSystem(definition, parentId);
            _repo.Add(account);
            byCode[definition.Code] = account;
        }
    }

    /// <summary>Idempotent: inserts missing system accounts and syncs name/parent/order from the catalog.</summary>
    public async Task EnsureDefaultsAsync(CancellationToken ct)
    {
        var existing = await _repo.ListAsync<ChartAccount>(c => c.IsSystem, ct);
        var byCode = existing.Where(c => c.Code is not null).ToDictionary(c => c.Code!);
        var changed = false;

        foreach (var definition in SystemChartAccounts.All.OrderBy(d => d.SortOrder))
        {
            Guid? parentId = definition.ParentCode is null
                ? null
                : byCode.TryGetValue(definition.ParentCode, out var parent) ? parent.Id : null;

            if (byCode.TryGetValue(definition.Code, out var account))
            {
                if (account.Name != definition.Name
                    || account.ParentId != parentId
                    || account.Level != definition.Level
                    || account.Section != definition.Section
                    || account.SortOrder != definition.SortOrder)
                {
                    account.SyncFromDefinition(definition, parentId);
                    changed = true;
                }
            }
            else
            {
                if (definition.ParentCode is not null && parentId is null)
                    continue;

                var created = ChartAccount.CreateSystem(definition, parentId);
                _repo.Add(created);
                byCode[definition.Code] = created;
                changed = true;
            }
        }

        if (changed)
            await _uow.SaveChangesAsync(ct);
    }

    public async Task<ChartAccount> GetByCodeAsync(string code, CancellationToken ct)
    {
        await EnsureDefaultsAsync(ct);
        return await _repo.FirstOrDefaultAsync<ChartAccount>(c => c.Code == code, ct, track: false)
               ?? throw new NotFoundException($"Conta de sistema '{code}' não encontrada.");
    }

    public async Task<IReadOnlyList<ChartAccountResponse>> ListAsync(
        ChartSection? section,
        bool includeInactive,
        bool analyticalOnly,
        CancellationToken ct)
    {
        await EnsureDefaultsAsync(ct);
        var items = await _repo.ListAsync<ChartAccount>(
            c => (section == null || c.Section == section)
                 && (includeInactive || c.IsActive)
                 && (!analyticalOnly || c.Level == ChartAccountLevel.Analytical),
            ct);

        return items
            .OrderBy(c => c.Section)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(ToResponse)
            .ToList();
    }

    public async Task<ChartAccountResponse> CreateAsync(CreateChartAccountRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        await EnsureDefaultsAsync(ct);

        var parent = await RequireAsync(request.ParentId, ct);
        if (parent.Level == ChartAccountLevel.Analytical)
            throw new ValidationException("parentId", "Conta analítica não pode ter filhos.");

        var sortOrder = (await _repo.ListAsync<ChartAccount>(c => c.ParentId == parent.Id, ct))
            .Select(c => c.SortOrder)
            .DefaultIfEmpty(parent.SortOrder)
            .Max() + 1;

        var account = ChartAccount.CreateAnalytical(request.Name, parent.Id, parent.Section, sortOrder);
        await EnsureUniqueNameAsync(account.Name, parent.Id, null, ct);

        _repo.Add(account);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(account);
    }

    public async Task<ChartAccountResponse> UpdateAsync(Guid id, UpdateChartAccountRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var account = await RequireAsync(id, ct);
        account.Rename(request.Name);
        account.SetActive(request.IsActive);
        await EnsureUniqueNameAsync(account.Name, account.ParentId ?? Guid.Empty, account.Id, ct);

        await _uow.SaveChangesAsync(ct);
        return ToResponse(account);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var account = await RequireAsync(id, ct);
        account.EnsureNotSystem();
        account.EnsureAnalytical();

        if (await _repo.AnyAsync<Entry>(e => e.ChartAccountId == id, ct)
            || await _repo.AnyAsync<CardPurchase>(p => p.ChartAccountId == id, ct)
            || await _repo.AnyAsync<BudgetLine>(l => l.ChartAccountId == id, ct)
            || await _repo.AnyAsync<PatrimonyItem>(p => p.ChartAccountId == id, ct))
            throw new ValidationException("id", "Conta em uso. Desative-a em vez de excluir.");

        _repo.SoftDelete(account);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ChartAccount> RequireAnalyticalAsync(Guid id, CancellationToken ct)
    {
        var account = await RequireAsync(id, ct);
        account.EnsureAnalytical();
        if (!account.IsActive)
            throw new ValidationException("chartAccountId", "Conta inativa.");
        return account;
    }

    private async Task<ChartAccount> RequireAsync(Guid id, CancellationToken ct)
        => await _repo.GetAsync<ChartAccount>(id, ct) ?? throw new NotFoundException("Conta do plano", id);

    private async Task EnsureUniqueNameAsync(string name, Guid parentId, Guid? ignoreId, CancellationToken ct)
    {
        var lowered = name.ToLowerInvariant();
        var clash = await _repo.AnyAsync<ChartAccount>(
            c => c.ParentId == parentId && c.Id != ignoreId && c.Name.ToLower() == lowered, ct);
        if (clash)
            throw new ValidationException("name", "Já existe uma conta com este nome neste grupo.");
    }

    private static ChartAccountResponse ToResponse(ChartAccount c)
        => new(c.Id, c.ParentId, c.Name, c.Code, c.Level, c.Section, c.IsSystem, c.IsActive, c.SortOrder, c.AcceptsPosting);
}
