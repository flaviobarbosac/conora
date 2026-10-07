using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
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

    /// <summary>Adds the system categories to the context without saving (used when a new user registers).</summary>
    public void AddDefaults()
        => _repo.AddRange(SystemCategories.All.Select(Category.CreateSystem));

    /// <summary>Idempotent: inserts missing system categories and syncs block/group from the catalog.</summary>
    public async Task EnsureDefaultsAsync(CancellationToken ct)
    {
        var existing = await _repo.ListAsync<Category>(c => c.IsSystem, ct);
        var byCode = existing.Where(c => c.Code is not null).ToDictionary(c => c.Code!);
        var changed = false;

        foreach (var definition in SystemCategories.All)
        {
            if (byCode.TryGetValue(definition.Code, out var category))
            {
                if (category.BudgetBlock != definition.Block
                    || category.GroupName != definition.GroupName
                    || category.Name != definition.Name
                    || category.IsEssential != definition.IsEssential)
                {
                    category.SyncFromDefinition(definition);
                    changed = true;
                }
            }
            else
            {
                _repo.Add(Category.CreateSystem(definition));
                changed = true;
            }
        }

        if (changed)
            await _uow.SaveChangesAsync(ct);
    }

    public async Task<Category> GetByCodeAsync(string code, CancellationToken ct)
    {
        await EnsureDefaultsAsync(ct);
        return await _repo.FirstOrDefaultAsync<Category>(c => c.Code == code, ct, track: false)
               ?? throw new NotFoundException($"Categoria de sistema '{code}' não encontrada.");
    }

    public async Task<IReadOnlyList<CategoryResponse>> ListAsync(CategoryKind? kind, bool includeInactive, CancellationToken ct)
    {
        await EnsureDefaultsAsync(ct);
        var items = await _repo.ListAsync<Category>(
            c => (kind == null || c.Kind == kind) && (includeInactive || c.IsActive), ct);
        return items.OrderBy(c => c.Kind).ThenBy(c => c.GroupName).ThenBy(c => c.Name).Select(ToResponse).ToList();
    }

    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var category = Category.Create(request.Name, request.Kind, request.IsEssential, request.BudgetBlock, request.GroupName);
        await EnsureUniqueNameAsync(category.Name, category.Kind, null, ct);

        _repo.Add(category);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(category);
    }

    public async Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var category = await RequireAsync(id, ct);
        category.Update(request.Name, request.IsEssential, request.BudgetBlock, request.GroupName);
        category.SetActive(request.IsActive);
        await EnsureUniqueNameAsync(category.Name, category.Kind, category.Id, ct);

        await _uow.SaveChangesAsync(ct);
        return ToResponse(category);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var category = await RequireAsync(id, ct);
        category.EnsureNotSystem();

        if (await _repo.AnyAsync<Entry>(e => e.CategoryId == id, ct))
            throw new ValidationException("id", "Categoria em uso por lançamentos. Desative-a em vez de excluir.");

        _repo.SoftDelete(category);
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<Category> RequireAsync(Guid id, CancellationToken ct)
        => await _repo.GetAsync<Category>(id, ct) ?? throw new NotFoundException("Categoria", id);

    private async Task EnsureUniqueNameAsync(string name, CategoryKind kind, Guid? ignoreId, CancellationToken ct)
    {
        var lowered = name.ToLowerInvariant();
        var clash = await _repo.AnyAsync<Category>(
            c => c.Kind == kind && c.Id != ignoreId && c.Name.ToLower() == lowered, ct);
        if (clash)
            throw new ValidationException("name", "Já existe uma categoria com este nome.");
    }

    private static CategoryResponse ToResponse(Category c)
        => new(c.Id, c.Name, c.Code, c.Kind, c.IsSystem, c.IsActive, c.IsEssential, c.BudgetBlock, c.GroupName);
}
