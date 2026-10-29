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

        // Free tier: only unlocked analytical leaves (+ pending when requested) and their ancestors.
        if (!await _plan.HasFullAccessAsync(ct))
            items = FilterFreeTierVisible(items, includePending);

        var names = await CreatorNamesAsync(items, ct);
        var self = _tenant.UsuarioId;
        return items
            .OrderBy(c => c.Section)
            .ThenBy(c => c.Level == CategoryLevel.Root ? 0 : 1)
            .ThenBy(c => c.Name, NameComparer)
            .Select(c => ToResponse(c, CreatorName(c, self, names)))
            .ToList();
    }

    /// <summary>
    /// Keeps free-tier analytical codes (and optional pending customs) plus ancestor groups/roots.
    /// </summary>
    internal static List<Category> FilterFreeTierVisible(IReadOnlyList<Category> items, bool includePending)
    {
        var byId = items.ToDictionary(c => c.Id);
        var keep = new HashSet<Guid>();
        foreach (var account in items)
        {
            var unlocked = FreeTierLimits.AllowsCategoryCode(account.Code);
            var pending = includePending && account.IsPendingReview;
            if (!unlocked && !pending)
                continue;

            Category? current = account;
            while (current is not null)
            {
                keep.Add(current.Id);
                current = current.ParentId is Guid parentId && byId.TryGetValue(parentId, out var parent)
                    ? parent
                    : null;
            }
        }

        return items.Where(c => keep.Contains(c.Id)).ToList();
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
}
