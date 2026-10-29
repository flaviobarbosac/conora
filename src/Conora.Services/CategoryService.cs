using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.CategoryAccess;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed partial class CategoryService
{
    private static readonly StringComparer NameComparer =
        StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true);

    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly PlanService _plan;
    private readonly IFamilyGroupRepository _family;
    private readonly ITenantContext _tenant;
    private readonly ICategoryAccessPolicyFactory _categoryAccess;

    public CategoryService(
        IFinanceRepository repo,
        IUnitOfWork uow,
        PlanService plan,
        IFamilyGroupRepository family,
        ITenantContext tenant,
        ICategoryAccessPolicyFactory categoryAccess)
    {
        _repo = repo;
        _uow = uow;
        _plan = plan;
        _family = family;
        _tenant = tenant;
        _categoryAccess = categoryAccess;
    }

    /// <summary>Adds the system categories without saving (used when a new user registers).</summary>

    /// <summary>Idempotent: inserts missing system accounts and syncs name/parent/order from the catalog.</summary>
    ICategoryAccessPolicy AccessFor(bool fullCategoryAccess) =>
        _categoryAccess.ForFullAccess(fullCategoryAccess);

    

    

    /// <summary>
    /// Toggles IsActive only — allowed for system analytical accounts (name/delete stay protected).
    /// </summary>

    /// <summary>
    /// On invite accept: copy the owner's accepted chart to the invitee, then bring invitee-only
    /// custom categories onto the owner as pending review.
    /// </summary>
    

    

    

    

    private async Task UpsertReplicaAsync(
        List<Category> all,
        List<Category> active,
        Guid usuarioId,
        string parentCode,
        string name,
        string? displayNumber,
        CategorySection section,
        bool isActive,
        int sortOrder,
        Guid createdBy,
        Guid familyKey,
        bool isPending,
        CancellationToken ct)
    {
        var parent = active.FirstOrDefault(c =>
            c.UsuarioId == usuarioId && c.IsSystem && c.Code == parentCode && c.DeletedAt is null);
        if (parent is null)
            return;

        var align = new CategoryReplicaAlign(
            parent.Id, name, displayNumber, section, isActive, sortOrder, createdBy, familyKey, isPending);

        var existing = active.FirstOrDefault(c =>
            c.UsuarioId == usuarioId
            && !c.IsSystem
            && (c.FamilyKey == familyKey || (c.ParentId == parent.Id && NameComparer.Equals(c.Name, name))));

        if (existing is not null)
        {
            await _repo.AlignCategoryReplicaAsync(existing.Id, align, ct);
            return;
        }

        var deleted = all.FirstOrDefault(c =>
            c.UsuarioId == usuarioId
            && c.DeletedAt is not null
            && (c.FamilyKey == familyKey
                || (c.ParentId == parent.Id && NameComparer.Equals(c.Name, name))));
        if (deleted is not null)
        {
            await _repo.RestoreCategoryReplicaAsync(deleted.Id, align, ct);
            return;
        }

        await _repo.InsertCategoryReplicaAsync(
            new CategoryReplicaInsert(
                Guid.CreateVersion7(),
                usuarioId,
                parent.Id,
                name,
                displayNumber,
                section,
                isActive,
                sortOrder,
                createdBy,
                familyKey,
                isPending),
            ct);
    }

    private static CategoryReplicaAlign AlignFrom(
        Category source,
        Guid parentId,
        Guid createdBy,
        Guid familyKey,
        bool pending)
        => new(
            parentId,
            source.Name,
            source.DisplayNumber,
            source.Section,
            source.IsActive,
            source.SortOrder,
            createdBy,
            familyKey,
            pending);

    private static Category? FindByParentCodeAndName(
        List<Category> active,
        Guid usuarioId,
        string parentCode,
        string name,
        Dictionary<Guid, Category> byId)
        => active.FirstOrDefault(c =>
            c.UsuarioId == usuarioId
            && !c.IsSystem
            && c.Level == CategoryLevel.Analytical
            && ParentCode(c, byId) == parentCode
            && NameComparer.Equals(c.Name, name));

    private async Task RenumberForUsuarioAsync(Guid usuarioId, CancellationToken ct)
    {
        var accounts = (await _repo.ListGroupCategoriesAsync([usuarioId], ct))
            .Where(c => c.DeletedAt is null)
            .ToList();
        if (accounts.Count == 0)
            return;

        CategoryDisplayNumbers.Apply(accounts);
        foreach (var account in accounts)
            await _repo.SetCategoryDisplayNumberAsync(account.Id, account.DisplayNumber, ct);
    }

    private async Task EnsureCanManageChartAsync(CancellationToken ct)
    {
        var self = RequireUserId();
        var membership = await _family.FindActiveMembershipAsync(self, ct);
        if (membership is null)
            return;

        var group = await _family.GetGroupAsync(membership.FamilyGroupId, ct, track: false)
                    ?? throw new ValidationException("group", "Grupo não encontrado.");
        if (!group.IsBudgetOwner(self))
            throw new ForbiddenException("Só o responsável pelo orçamento pode alterar o plano de categorias.");
    }

    private async Task EnsureBudgetOwnerAsync(CancellationToken ct)
    {
        var self = RequireUserId();
        var membership = await _family.FindActiveMembershipAsync(self, ct)
                         ?? throw new ForbiddenException("Só o responsável pelo orçamento pode analisar categorias.");
        var group = await _family.GetGroupAsync(membership.FamilyGroupId, ct, track: false)
                    ?? throw new ValidationException("group", "Grupo não encontrado.");
        if (!group.IsBudgetOwner(self))
            throw new ForbiddenException("Só o responsável pelo orçamento pode analisar categorias.");
    }

    private async Task<List<Guid>> ActiveMemberIdsForOwnerAsync(Guid ownerUsuarioId, CancellationToken ct)
    {
        var membership = await _family.FindActiveMembershipAsync(ownerUsuarioId, ct);
        if (membership is null)
            return [ownerUsuarioId];

        return (await _family.ListActiveMembersAsync(membership.FamilyGroupId, ct))
            .Select(m => m.UsuarioId)
            .Distinct()
            .ToList();
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

    private async Task<bool> RetireObsoleteAsync(Dictionary<string, Category> byCode, CancellationToken ct)
    {
        var catalog = SystemCategories.All.Select(d => d.Code).ToHashSet(StringComparer.Ordinal);
        var obsolete = byCode.Where(kv => !catalog.Contains(kv.Key)).Select(kv => kv.Value).ToList();
        if (obsolete.Count == 0)
            return false;

        var all = await _repo.ListAsync<Category>(ct: ct, track: true);
        foreach (var (fromCode, toCode) in SystemCategories.Replacements)
        {
            if (!byCode.TryGetValue(fromCode, out var from) || !byCode.TryGetValue(toCode, out var to))
                continue;

            if (from.Level == CategoryLevel.Analytical && to.AcceptsPosting)
                await MovePostingsAsync(from, to, ct);

            var parentId = to.Level == CategoryLevel.Analytical ? to.ParentId : to.Id;
            if (parentId is Guid pid)
            {
                foreach (var child in all.Where(c => !c.IsSystem && c.ParentId == from.Id && c.DeletedAt is null))
                    child.Reparent(pid, to.Section);
            }

            await RetargetReceiptsAsync(fromCode, toCode, ct);
        }

        foreach (var old in obsolete)
        {
            var parent = old.ParentId is Guid pid
                ? all.FirstOrDefault(c => c.Id == pid && c.DeletedAt is null && catalog.Contains(c.Code ?? ""))
                : null;
            if (parent is null)
                continue;

            foreach (var child in all.Where(c => !c.IsSystem && c.ParentId == old.Id && c.DeletedAt is null))
                child.Reparent(parent.Id, parent.Section);
        }

        foreach (var old in obsolete.OrderByDescending(c => c.Level))
        {
            if (old.Level == CategoryLevel.Analytical && await HasPostingsAsync(old.Id, ct))
                continue;

            if (all.Any(c => c.Id != old.Id && c.ParentId == old.Id && c.DeletedAt is null))
                continue;

            _repo.SoftDelete(old);
        }

        return true;
    }

    private async Task<bool> HasPostingsAsync(Guid categoryId, CancellationToken ct)
        => await _repo.AnyAsync<Entry>(e => e.CategoryId == categoryId, ct)
           || await _repo.AnyAsync<CardPurchase>(p => p.CategoryId == categoryId, ct)
           || await _repo.AnyAsync<BudgetLine>(l => l.CategoryId == categoryId, ct)
           || await _repo.AnyAsync<PatrimonyItem>(p => p.CategoryId == categoryId, ct)
           || await _repo.AnyAsync<LifeProject>(p => p.CategoryId == categoryId, ct);

    private async Task MovePostingsAsync(Category from, Category to, CancellationToken ct)
    {
        foreach (var entry in await _repo.ListAsync<Entry>(e => e.CategoryId == from.Id, ct, track: true))
            entry.MoveCategory(to.Id);

        foreach (var purchase in await _repo.ListAsync<CardPurchase>(p => p.CategoryId == from.Id, ct, track: true))
            purchase.MoveCategory(to.Id);

        foreach (var item in await _repo.ListAsync<PatrimonyItem>(p => p.CategoryId == from.Id, ct, track: true))
            item.SetCategory(to.Id);

        foreach (var project in await _repo.ListAsync<LifeProject>(p => p.CategoryId == from.Id, ct, track: true))
            project.MoveCategory(to.Id);

        var incoming = await _repo.ListAsync<BudgetLine>(l => l.CategoryId == from.Id, ct, track: true);
        foreach (var line in incoming)
        {
            var clash = await _repo.FirstOrDefaultAsync<BudgetLine>(
                l => l.BudgetId == line.BudgetId && l.CategoryId == to.Id && l.Id != line.Id, ct, track: true);
            if (clash is not null)
            {
                clash.SetPlanned(clash.PlannedAmount + line.PlannedAmount);
                if (clash.ExpenseDay is null)
                    clash.SetExpenseDay(line.ExpenseDay);
                _repo.SoftDelete(line);
            }
            else
            {
                line.MoveCategory(to.Id);
            }
        }

        await _uow.SaveChangesAsync(ct);
    }

    private async Task RetargetReceiptsAsync(string fromCode, string toCode, CancellationToken ct)
    {
        var receipts = await _repo.ListAsync<ReceiptIntake>(r => r.ProposedCategoryCode == fromCode, ct, track: true);
        foreach (var receipt in receipts)
            receipt.RetargetProposedCode(toCode);
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

    [Obsolete("Category Personal/Family sharing was removed. Chart is shared with pending review.")]

    private async Task<List<Guid>> ReplicaIdsAsync(Category account, IReadOnlyList<Guid> memberIds, CancellationToken ct)
    {
        var ids = new List<Guid> { account.Id };
        if (account.FamilyKey is not Guid familyKey || memberIds.Count < 2)
            return ids;

        var rows = await _repo.ListGroupCategoriesAsync(memberIds, ct);
        ids.AddRange(rows.Where(c => c.FamilyKey == familyKey && c.DeletedAt is null).Select(c => c.Id));
        return ids.Distinct().ToList();
    }

    private async Task<List<Guid>> MemberIdsAsync(CancellationToken ct)
    {
        var self = RequireUserId();
        var membership = await _family.FindActiveMembershipAsync(self, ct);
        if (membership is null)
            return [self];

        var ids = (await _family.ListActiveMembersAsync(membership.FamilyGroupId, ct))
            .Select(m => m.UsuarioId)
            .Distinct()
            .ToList();
        return ids.Count == 0 ? [self] : ids;
    }

    private async Task<Dictionary<Guid, string>> CreatorNamesAsync(IReadOnlyList<Category> items, CancellationToken ct)
    {
        var self = _tenant.UsuarioId;
        var ownerIds = items
            .Where(c => !c.IsSystem && c.CreatedByUsuarioId is Guid id && id != self)
            .Select(c => c.CreatedByUsuarioId!.Value)
            .Distinct()
            .ToList();
        if (ownerIds.Count == 0)
            return [];

        var users = await _family.ListUsersByIdsAsync(ownerIds, ct);
        return users.ToDictionary(u => u.Id, u => u.Name);
    }

    private static string? CreatorName(Category category, Guid? self, IReadOnlyDictionary<Guid, string> names)
    {
        if (category.IsSystem || category.CreatedByUsuarioId is not Guid owner || owner == self)
            return null;
        return names.TryGetValue(owner, out var name) ? name : null;
    }

    private Guid RequireUserId()
        => _tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");

    private static string? ParentCode(Category category, IReadOnlyDictionary<Guid, Category> byId)
    {
        if (category.ParentId is not Guid parentId)
            return null;
        return byId.TryGetValue(parentId, out var parent) ? parent.Code : null;
    }

    private static CategoryResponse ToResponse(Category c, string? createdByName = null)
        => new(
            c.Id,
            c.ParentId,
            c.Name,
            c.Code,
            c.DisplayNumber,
            c.Level,
            c.Section,
            c.IsSystem,
            c.IsActive,
            c.SortOrder,
            c.AcceptsPosting,
            c.SharingScope,
            c.CreatedByUsuarioId,
            createdByName,
            c.IsPendingReview);
}
