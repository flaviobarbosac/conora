using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed partial class CategoryService
{
    public async Task<IReadOnlyList<CategoryResponse>> ListAsync(
        CategorySection? section,
        bool includeInactive,
        bool analyticalOnly,
        CancellationToken ct,
        bool includePending = false)
    {
        await EnsureDefaultsAsync(ct);
        var items = await _repo.ListAsync<Category>(
            c => (section == null || c.Section == section)
                 && (includeInactive || c.IsActive)
                 && (!analyticalOnly || c.Level == CategoryLevel.Analytical)
                 && (includePending || !c.IsPendingReview),
            ct);

        var names = await CreatorNamesAsync(items, ct);
        var self = _tenant.UsuarioId;
        return items
            .OrderBy(c => c.Section)
            .ThenBy(c => c.Level == CategoryLevel.Root ? 0 : 1)
            .ThenBy(c => c.Name, NameComparer)
            .Select(c => ToResponse(c, CreatorName(c, self, names)))
            .ToList();
    }

    public async Task<Category> RequireAnalyticalAsync(Guid id, CancellationToken ct)
    {
        var account = await RequireAsync(id, ct);
        account.EnsureAnalytical();
        if (!account.IsActive)
            throw new ValidationException("categoryId", "Conta inativa.");
        if (account.IsPendingReview)
            throw new ValidationException("categoryId", "Categoria sob análise. Aceite-a antes de lançar.");
        return account;
    }

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
}
