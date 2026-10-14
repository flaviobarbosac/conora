using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class CategoryService
{
    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly PlanService _plan;
    private readonly IFamilyGroupRepository _family;
    private readonly ITenantContext _tenant;

    public CategoryService(
        IFinanceRepository repo,
        IUnitOfWork uow,
        PlanService plan,
        IFamilyGroupRepository family,
        ITenantContext tenant)
    {
        _repo = repo;
        _uow = uow;
        _plan = plan;
        _family = family;
        _tenant = tenant;
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

        if (await RetireObsoleteAsync(byCode, ct))
            changed = true;

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
        await SyncFamilyCategoriesAsync(ct);
        var items = await _repo.ListAsync<Category>(
            c => (section == null || c.Section == section)
                 && (includeInactive || c.IsActive)
                 && (!analyticalOnly || c.Level == CategoryLevel.Analytical),
            ct);

        var names = await CreatorNamesAsync(items, ct);
        var self = _tenant.UsuarioId;
        var nameOrder = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true);
        return items
            .OrderBy(c => c.Section)
            .ThenBy(c => c.Level == CategoryLevel.Root ? 0 : 1)
            .ThenBy(c => c.Name, nameOrder)
            .Select(c => ToResponse(c, CreatorName(c, self, names)))
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
                "Nova conta só pode ficar sob Renda, Descontos sobre renda, Projetos de vida, Essencial, Social, Ativo ou Passivo.");

        var sortOrder = (await _repo.ListAsync<Category>(c => c.ParentId == parent.Id, ct))
            .Select(c => c.SortOrder)
            .DefaultIfEmpty(parent.SortOrder)
            .Max() + 1;

        var account = Category.CreateAnalytical(request.Name, parent.Id, parent.Section, sortOrder);
        account.AssignFamilyIdentity(RequireUserId(), Guid.CreateVersion7());
        await EnsureUniqueNameAsync(account.Name, parent.Id, null, ct);

        _repo.Add(account);
        await _uow.SaveChangesAsync(ct);
        await RenumberAsync(ct);
        await SyncFamilyCategoriesAsync(ct);
        var numbered = await RequireAsync(account.Id, ct);
        return ToResponse(numbered);
    }

    public async Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var account = await RequireAsync(id, ct);
        EnsureCreator(account);
        account.Rename(request.Name);
        account.SetActive(request.IsActive);
        if (request.SharingScope is SharingScope scope)
            account.SetSharingScope(scope);
        await EnsureUniqueNameAsync(account.Name, account.ParentId ?? Guid.Empty, account.Id, ct);

        await _uow.SaveChangesAsync(ct);
        if (account.SharingScope == SharingScope.Personal)
            await DropFamilyReplicasAsync(account, ct);
        else
            await SyncFamilyCategoriesAsync(ct);
        return ToResponse(account);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var account = await RequireAsync(id, ct);
        account.EnsureNotSystem();
        account.EnsureAnalytical();
        EnsureCreator(account);

        var memberIds = await MemberIdsAsync(ct);
        var replicaIds = await ReplicaIdsAsync(account, memberIds, ct);
        if (await InUseAsync(replicaIds, memberIds, ct))
            throw new ValidationException("id", "Conta em uso. Desative-a em vez de excluir.");

        _repo.SoftDelete(account);
        await _uow.SaveChangesAsync(ct);
        if (account.FamilyKey is Guid familyKey && memberIds.Count > 1)
            await _repo.SoftDeleteFamilyReplicasAsync(familyKey, memberIds, account.Id, ct);
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

    public async Task<CategoryResponse> UpdateSharingAsync(Guid id, UpdateCategorySharingRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var account = await RequireAsync(id, ct);
        if (!account.IsSystem)
            EnsureCreator(account);
        account.SetSharingScope(request.SharingScope);
        await _uow.SaveChangesAsync(ct);
        if (account.IsSystem)
            return ToResponse(account);

        if (request.SharingScope == SharingScope.Personal)
            await DropFamilyReplicasAsync(account, ct);
        else
            await SyncFamilyCategoriesAsync(ct);
        return ToResponse(account);
    }

    /// <summary>
    /// Copies every Family analytical category onto every member of the current group.
    /// New members therefore receive the owner's chart, including categories created by someone else.
    /// </summary>
    public async Task SyncFamilyCategoriesAsync(CancellationToken ct)
    {
        var self = _tenant.UsuarioId;
        if (self is null)
            return;

        var membership = await _family.FindActiveMembershipAsync(self.Value, ct);
        if (membership is null)
            return;

        var memberIds = (await _family.ListActiveMembersAsync(membership.FamilyGroupId, ct))
            .Select(m => m.UsuarioId)
            .Distinct()
            .ToList();
        if (memberIds.Count < 2)
            return;

        var all = await _repo.ListGroupCategoriesAsync(memberIds, ct);
        var active = all.Where(c => c.DeletedAt is null).ToList();
        var byId = active.ToDictionary(c => c.Id);
        var familyCustom = active
            .Where(c => !c.IsSystem && c.Level == CategoryLevel.Analytical && c.SharingScope == SharingScope.Family)
            .ToList();
        if (familyCustom.Count == 0)
            return;

        var changed = false;
        foreach (var cluster in MergeClusters(familyCustom, byId))
        {
            var origin = cluster.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).First();
            var creators = cluster.Select(c => c.CreatedByUsuarioId ?? c.UsuarioId).Distinct().ToList();
            // One creator already: keep them. Independent copies of the same name: the earliest row wins.
            var createdBy = creators.Count == 1 ? creators[0] : origin.UsuarioId;
            var source = cluster.FirstOrDefault(c => c.UsuarioId == createdBy) ?? origin;
            var familyKey = (creators.Count == 1 ? source.FamilyKey : origin.FamilyKey) ?? origin.Id;
            var parentCode = ParentCode(source, byId);
            if (string.IsNullOrEmpty(parentCode))
                continue;

            foreach (var memberId in memberIds)
            {
                var parent = active.FirstOrDefault(c =>
                    c.UsuarioId == memberId && c.IsSystem && c.Code == parentCode && c.DeletedAt is null);
                if (parent is null)
                    continue;

                var align = new CategoryReplicaAlign(
                    parent.Id,
                    source.Name,
                    source.DisplayNumber,
                    source.Section,
                    source.IsActive,
                    source.SortOrder,
                    createdBy,
                    familyKey);

                var row = cluster.FirstOrDefault(c => c.UsuarioId == memberId && c.DeletedAt is null);
                if (row is null)
                {
                    var deleted = all.FirstOrDefault(c =>
                        c.UsuarioId == memberId && c.DeletedAt is not null && c.FamilyKey == familyKey);
                    if (deleted is not null)
                    {
                        await _repo.RestoreCategoryReplicaAsync(deleted.Id, align, ct);
                        changed = true;
                        continue;
                    }

                    var clash = active.Any(c =>
                        c.UsuarioId == memberId
                        && c.ParentId == parent.Id
                        && !c.IsSystem
                        && c.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase));
                    if (clash)
                        continue;

                    await _repo.InsertCategoryReplicaAsync(
                        new CategoryReplicaInsert(
                            Guid.CreateVersion7(),
                            memberId,
                            parent.Id,
                            source.Name,
                            source.DisplayNumber,
                            source.Section,
                            source.IsActive,
                            source.SortOrder,
                            createdBy,
                            familyKey),
                        ct);
                    changed = true;
                    continue;
                }

                if (row.FamilyKey == familyKey
                    && row.CreatedByUsuarioId == createdBy
                    && row.Name == source.Name
                    && row.IsActive == source.IsActive
                    && row.ParentId == parent.Id
                    && row.SharingScope == SharingScope.Family)
                    continue;

                await _repo.AlignCategoryReplicaAsync(row.Id, align, ct);
                changed = true;
            }
        }

        if (changed)
            await RenumberAsync(ct);
    }

    private async Task DropFamilyReplicasAsync(Category account, CancellationToken ct)
    {
        if (account.FamilyKey is not Guid familyKey)
            return;

        var memberIds = await MemberIdsAsync(ct);
        if (memberIds.Count < 2)
            return;

        await _repo.SoftDeleteFamilyReplicasAsync(familyKey, memberIds, account.Id, ct);
    }

    private async Task<bool> InUseAsync(IReadOnlyList<Guid> categoryIds, IReadOnlyList<Guid> memberIds, CancellationToken ct)
    {
        if (categoryIds.Count == 0)
            return false;

        if (memberIds.Count < 2)
        {
            return await _repo.AnyAsync<Entry>(e => e.CategoryId != null && categoryIds.Contains(e.CategoryId.Value), ct)
                   || await _repo.AnyAsync<CardPurchase>(p => categoryIds.Contains(p.CategoryId), ct)
                   || await _repo.AnyAsync<BudgetLine>(l => categoryIds.Contains(l.CategoryId), ct)
                   || await _repo.AnyAsync<PatrimonyItem>(p => categoryIds.Contains(p.CategoryId), ct);
        }

        return await _repo.AnyForUsuariosAsync<Entry>(
                   memberIds, e => e.CategoryId != null && categoryIds.Contains(e.CategoryId.Value), ct)
               || await _repo.AnyForUsuariosAsync<CardPurchase>(memberIds, p => categoryIds.Contains(p.CategoryId), ct)
               || await _repo.AnyForUsuariosAsync<BudgetLine>(memberIds, l => categoryIds.Contains(l.CategoryId), ct)
               || await _repo.AnyForUsuariosAsync<PatrimonyItem>(memberIds, p => categoryIds.Contains(p.CategoryId), ct);
    }

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

    private void EnsureCreator(Category account)
    {
        if (account.IsSystem)
            return;

        var owner = account.CreatedByUsuarioId ?? account.UsuarioId;
        if (owner != RequireUserId())
            throw new ForbiddenException("Só quem criou esta categoria pode alterá-la.");
    }

    private static List<List<Category>> MergeClusters(List<Category> familyCustom, Dictionary<Guid, Category> byId)
    {
        var keyed = familyCustom
            .GroupBy(c => c.FamilyKey ?? c.Id)
            .Select(g => g.ToList())
            .ToList();

        var merged = new Dictionary<string, List<Category>>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<List<Category>>();
        foreach (var cluster in keyed)
        {
            var sharedKey = cluster.Select(c => c.FamilyKey).Distinct().Count() == 1
                            && cluster[0].FamilyKey is not null
                            && cluster.Count > 1;
            if (sharedKey)
            {
                kept.Add(cluster);
                continue;
            }

            var origin = cluster.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).First();
            var code = ParentCode(origin, byId) ?? "";
            var mergeKey = code + "\n" + origin.Name.Trim();
            if (!merged.TryGetValue(mergeKey, out var bucket))
            {
                bucket = [];
                merged[mergeKey] = bucket;
            }

            bucket.AddRange(cluster);
        }

        kept.AddRange(merged.Values);
        return kept;
    }

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
            createdByName);
}
