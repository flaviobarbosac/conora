using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class CategoryService
{
    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly PlanService _plan;

    public CategoryService(IFinanceRepository repo, IUnitOfWork uow, PlanService plan)
    {
        _repo = repo;
        _uow = uow;
        _plan = plan;
    }

    /// <summary>Adds the system categories without saving (used when a new user registers).</summary>
    public void AddDefaults()
    {
        var byCode = new Dictionary<string, Category>(StringComparer.Ordinal);
        foreach (var definition in SystemCategories.All.OrderBy(d => d.SortOrder))
        {
            Guid? parentId = definition.ParentCode is null ? null : byCode[definition.ParentCode].Id;
            var account = Category.CreateSystem(definition, parentId);
            _repo.Add(account);
            byCode[definition.Code] = account;
        }

        CategoryDisplayNumbers.Apply(byCode.Values.ToList());
    }

    /// <summary>Idempotent: inserts missing system accounts and syncs name/parent/order from the catalog.</summary>
    public async Task EnsureDefaultsAsync(CancellationToken ct)
    {
        // Must track: SyncFromDefinition mutates existing rows (reparent/rename). AsNoTracking would no-op SaveChanges.
        var existing = await _repo.ListAsync<Category>(c => c.IsSystem, ct, track: true);
        var byCode = existing.Where(c => c.Code is not null).ToDictionary(c => c.Code!);
        var changed = false;

        foreach (var definition in SystemCategories.All.OrderBy(d => d.SortOrder))
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

                var created = Category.CreateSystem(definition, parentId);
                _repo.Add(created);
                byCode[definition.Code] = created;
                changed = true;
            }
        }

        if (changed)
        {
            await _uow.SaveChangesAsync(ct);
            await RenumberAsync(ct);
        }
        else
        {
            await RenumberIfNeededAsync(ct);
        }
    }

    public async Task<Category> GetByCodeAsync(string code, CancellationToken ct)
    {
        await EnsureDefaultsAsync(ct);
        return await _repo.FirstOrDefaultAsync<Category>(c => c.Code == code, ct, track: false)
               ?? throw new NotFoundException($"Conta de sistema '{code}' não encontrada.");
    }

    public async Task<IReadOnlyList<CategoryResponse>> ListAsync(
        CategorySection? section,
        bool includeInactive,
        bool analyticalOnly,
        CancellationToken ct)
    {
        await EnsureDefaultsAsync(ct);
        var items = await _repo.ListAsync<Category>(
            c => (section == null || c.Section == section)
                 && (includeInactive || c.IsActive)
                 && (!analyticalOnly || c.Level == CategoryLevel.Analytical),
            ct);

        var nameOrder = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true);
        return items
            .OrderBy(c => c.Section)
            .ThenBy(c => c.Level == CategoryLevel.Root ? 0 : 1)
            .ThenBy(c => c.Name, nameOrder)
            .Select(ToResponse)
            .ToList();
    }

    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        await EnsureDefaultsAsync(ct);

        var parent = await RequireAsync(request.ParentId, ct);
        if (parent.Level == CategoryLevel.Analytical)
            throw new ValidationException("parentId", "Conta analítica não pode ter filhos.");
        if (!SystemCategories.AcceptsAnalyticalChild(parent.Section))
            throw new ValidationException(
                "parentId",
                "Nova conta só pode ficar sob Receita, Descontos, Projeto de vida, Essencial, Social, Ativo ou Passivo.");

        var sortOrder = (await _repo.ListAsync<Category>(c => c.ParentId == parent.Id, ct))
            .Select(c => c.SortOrder)
            .DefaultIfEmpty(parent.SortOrder)
            .Max() + 1;

        var account = Category.CreateAnalytical(request.Name, parent.Id, parent.Section, sortOrder);
        await EnsureUniqueNameAsync(account.Name, parent.Id, null, ct);

        _repo.Add(account);
        await _uow.SaveChangesAsync(ct);
        await RenumberAsync(ct);
        var numbered = await RequireAsync(account.Id, ct);
        return ToResponse(numbered);
    }

    public async Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct)
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

        if (await _repo.AnyAsync<Entry>(e => e.CategoryId == id, ct)
            || await _repo.AnyAsync<CardPurchase>(p => p.CategoryId == id, ct)
            || await _repo.AnyAsync<BudgetLine>(l => l.CategoryId == id, ct)
            || await _repo.AnyAsync<PatrimonyItem>(p => p.CategoryId == id, ct))
            throw new ValidationException("id", "Conta em uso. Desative-a em vez de excluir.");

        _repo.SoftDelete(account);
        await _uow.SaveChangesAsync(ct);
        await RenumberAsync(ct);
    }

    public async Task<Category> RequireAnalyticalAsync(Guid id, CancellationToken ct)
    {
        var account = await RequireAsync(id, ct);
        account.EnsureAnalytical();
        if (!account.IsActive)
            throw new ValidationException("categoryId", "Conta inativa.");
        return account;
    }

    private async Task<Category> RequireAsync(Guid id, CancellationToken ct)
        => await _repo.GetAsync<Category>(id, ct) ?? throw new NotFoundException("Conta do plano", id);

    private async Task EnsureUniqueNameAsync(string name, Guid parentId, Guid? ignoreId, CancellationToken ct)
    {
        var lowered = name.ToLowerInvariant();
        var clash = await _repo.AnyAsync<Category>(
            c => c.ParentId == parentId && c.Id != ignoreId && c.Name.ToLower() == lowered, ct);
        if (clash)
            throw new ValidationException("name", "Já existe uma conta com este nome neste grupo.");
    }

    private async Task RenumberIfNeededAsync(CancellationToken ct)
    {
        var accounts = await _repo.ListAsync<Category>(ct: ct, track: true);
        if (accounts.Count == 0 || accounts.All(a => !string.IsNullOrWhiteSpace(a.DisplayNumber)))
            return;

        CategoryDisplayNumbers.Apply(accounts);
        await _uow.SaveChangesAsync(ct);
    }

    private async Task RenumberAsync(CancellationToken ct)
    {
        var accounts = await _repo.ListAsync<Category>(ct: ct, track: true);
        CategoryDisplayNumbers.Apply(accounts);
        await _uow.SaveChangesAsync(ct);
    }

    private static CategoryResponse ToResponse(Category c)
        => new(c.Id, c.ParentId, c.Name, c.Code, c.DisplayNumber, c.Level, c.Section, c.IsSystem, c.IsActive, c.SortOrder, c.AcceptsPosting);
}
