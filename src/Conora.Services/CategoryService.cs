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
    private static readonly StringComparer NameComparer =
        StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true);

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

    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        await EnsureCanManageChartAsync(ct);
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

        var self = RequireUserId();
        var account = Category.CreateAnalytical(request.Name, parent.Id, parent.Section, sortOrder);
        account.AssignFamilyIdentity(self, Guid.CreateVersion7());
        account.MarkFamily();
        await EnsureUniqueNameAsync(account.Name, parent.Id, null, ct);

        _repo.Add(account);
        await _uow.SaveChangesAsync(ct);
        await RenumberAsync(ct);
        await PropagateAcceptedAsync(account.Id, ct);
        var numbered = await RequireAsync(account.Id, ct);
        return ToResponse(numbered);
    }

    public async Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        await EnsureCanManageChartAsync(ct);
        var account = await RequireAsync(id, ct);
        if (account.IsPendingReview)
            throw new ValidationException("id", "Aceite ou recuse a categoria antes de editá-la.");

        // System analytical accounts may only toggle IsActive (name stays fixed).
        if (!account.IsSystem)
        {
            account.Rename(request.Name);
            account.MarkFamily();
            await EnsureUniqueNameAsync(account.Name, account.ParentId ?? Guid.Empty, account.Id, ct);
        }

        account.SetActive(request.IsActive);

        await _uow.SaveChangesAsync(ct);
        await PropagateAcceptedAsync(account.Id, ct);
        return ToResponse(account);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        await EnsureCanManageChartAsync(ct);
        var account = await RequireAsync(id, ct);
        account.EnsureNotSystem();
        account.EnsureAnalytical();
        if (account.IsPendingReview)
            throw new ValidationException("id", "Use Recusar para categorias sob análise.");

        var memberIds = await MemberIdsAsync(ct);
        var replicaIds = await ReplicaIdsAsync(account, memberIds, ct);
        if (await InUseAsync(replicaIds, memberIds, ct))
            throw new ValidationException("id", "Conta em uso. Desative-a em vez de excluir.");

        if (account.FamilyKey is Guid familyKey && memberIds.Count > 1)
            await _repo.SoftDeleteFamilyReplicasAsync(familyKey, memberIds, null, ct);
        else
        {
            _repo.SoftDelete(account);
            await _uow.SaveChangesAsync(ct);
        }

        await RenumberAsync(ct);
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

    public async Task<CategoryResponse> ApproveAsync(Guid id, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        await EnsureBudgetOwnerAsync(ct);
        var account = await RequireAsync(id, ct);
        account.EnsureNotSystem();
        account.EnsureAnalytical();
        if (!account.IsPendingReview)
            throw new ValidationException("id", "Esta categoria já está no plano.");

        account.AcceptReview();
        await _uow.SaveChangesAsync(ct);
        await PropagateAcceptedAsync(account.Id, ct);
        var numbered = await RequireAsync(account.Id, ct);
        return ToResponse(numbered);
    }

    public async Task RejectAsync(Guid id, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        await EnsureBudgetOwnerAsync(ct);
        var account = await RequireAsync(id, ct);
        account.EnsureNotSystem();
        account.EnsureAnalytical();
        if (!account.IsPendingReview)
            throw new ValidationException("id", "Só categorias sob análise podem ser recusadas.");

        var memberIds = await MemberIdsAsync(ct);
        var familyKey = account.FamilyKey ?? account.Id;
        await _repo.SoftDeleteFamilyReplicasAsync(familyKey, memberIds, null, ct);
        if (account.DeletedAt is null)
        {
            _repo.SoftDelete(account);
            await _uow.SaveChangesAsync(ct);
        }

        await RenumberAsync(ct);
    }

    /// <summary>
    /// On invite accept: copy the owner's accepted chart to the invitee, then bring invitee-only
    /// custom categories onto the owner as pending review.
    /// </summary>
    public async Task MergeOnJoinAsync(Guid ownerUsuarioId, Guid inviteeUsuarioId, CancellationToken ct)
    {
        var memberIds = (await ActiveMemberIdsForOwnerAsync(ownerUsuarioId, ct)).ToList();
        if (!memberIds.Contains(inviteeUsuarioId))
            memberIds.Add(inviteeUsuarioId);

        var all = await _repo.ListGroupCategoriesAsync(memberIds, ct);
        var active = all.Where(c => c.DeletedAt is null).ToList();
        var byId = active.ToDictionary(c => c.Id);

        await CopyAcceptedChartAsync(ownerUsuarioId, inviteeUsuarioId, all, active, byId, ct);

        all = await _repo.ListGroupCategoriesAsync(memberIds, ct);
        active = all.Where(c => c.DeletedAt is null).ToList();
        byId = active.ToDictionary(c => c.Id);

        await BringInviteeDiffAsPendingAsync(ownerUsuarioId, inviteeUsuarioId, all, active, byId, ct);

        foreach (var usuarioId in memberIds.Distinct())
            await RenumberForUsuarioAsync(usuarioId, ct);
    }

    private async Task CopyAcceptedChartAsync(
        Guid ownerUsuarioId,
        Guid inviteeUsuarioId,
        List<Category> all,
        List<Category> active,
        Dictionary<Guid, Category> byId,
        CancellationToken ct)
    {
        var ownerAccepted = active
            .Where(c => c.UsuarioId == ownerUsuarioId
                        && !c.IsSystem
                        && c.Level == CategoryLevel.Analytical
                        && !c.IsPendingReview)
            .ToList();

        foreach (var source in ownerAccepted)
        {
            var parentCode = ParentCode(source, byId);
            if (string.IsNullOrEmpty(parentCode))
                continue;

            if (FindByParentCodeAndName(active, inviteeUsuarioId, parentCode, source.Name, byId) is not null)
                continue;

            var familyKey = source.FamilyKey ?? source.Id;
            if (source.FamilyKey is null)
            {
                await _repo.AlignCategoryReplicaAsync(
                    source.Id,
                    AlignFrom(source, source.ParentId!.Value, source.CreatedByUsuarioId ?? ownerUsuarioId, familyKey, false),
                    ct);
            }

            await UpsertReplicaAsync(
                all,
                active,
                inviteeUsuarioId,
                parentCode,
                source.Name,
                source.DisplayNumber,
                source.Section,
                source.IsActive,
                source.SortOrder,
                source.CreatedByUsuarioId ?? ownerUsuarioId,
                familyKey,
                isPending: false,
                ct);
        }
    }

    private async Task BringInviteeDiffAsPendingAsync(
        Guid ownerUsuarioId,
        Guid inviteeUsuarioId,
        List<Category> all,
        List<Category> active,
        Dictionary<Guid, Category> byId,
        CancellationToken ct)
    {
        var inviteeCustom = active
            .Where(c => c.UsuarioId == inviteeUsuarioId
                        && !c.IsSystem
                        && c.Level == CategoryLevel.Analytical)
            .ToList();

        foreach (var inviteeCat in inviteeCustom)
        {
            var parentCode = ParentCode(inviteeCat, byId);
            if (string.IsNullOrEmpty(parentCode))
                continue;

            var ownerMatch = FindByParentCodeAndName(active, ownerUsuarioId, parentCode, inviteeCat.Name, byId);
            if (ownerMatch is not null && !ownerMatch.IsPendingReview)
            {
                // Already on the accepted chart — align invitee identity and leave accepted.
                var familyKey = ownerMatch.FamilyKey ?? ownerMatch.Id;
                await _repo.AlignCategoryReplicaAsync(
                    inviteeCat.Id,
                    AlignFrom(inviteeCat, inviteeCat.ParentId!.Value, ownerMatch.CreatedByUsuarioId ?? ownerUsuarioId, familyKey, false),
                    ct);
                continue;
            }

            var familyKeyPending = ownerMatch?.FamilyKey
                                   ?? inviteeCat.FamilyKey
                                   ?? inviteeCat.Id;

            var ownerParent = active.FirstOrDefault(c =>
                c.UsuarioId == ownerUsuarioId && c.IsSystem && c.Code == parentCode);
            var deletedOwner = ownerParent is null
                ? null
                : all.FirstOrDefault(c =>
                    c.UsuarioId == ownerUsuarioId
                    && c.DeletedAt is not null
                    && !c.IsSystem
                    && c.Level == CategoryLevel.Analytical
                    && (c.FamilyKey == familyKeyPending
                        || (c.ParentId == ownerParent.Id && NameComparer.Equals(c.Name, inviteeCat.Name))));

            if (ownerMatch is null && deletedOwner is not null && ownerParent is not null)
            {
                await _repo.RestoreCategoryReplicaAsync(
                    deletedOwner.Id,
                    AlignFrom(inviteeCat, ownerParent.Id, inviteeUsuarioId, familyKeyPending, true),
                    ct);
            }
            else if (ownerMatch is null)
            {
                await UpsertReplicaAsync(
                    all,
                    active,
                    ownerUsuarioId,
                    parentCode,
                    inviteeCat.Name,
                    inviteeCat.DisplayNumber,
                    inviteeCat.Section,
                    inviteeCat.IsActive,
                    inviteeCat.SortOrder,
                    inviteeUsuarioId,
                    familyKeyPending,
                    isPending: true,
                    ct);
            }
            else
            {
                await _repo.AlignCategoryReplicaAsync(
                    ownerMatch.Id,
                    AlignFrom(inviteeCat, ownerMatch.ParentId!.Value, inviteeUsuarioId, familyKeyPending, true),
                    ct);
            }

            await _repo.AlignCategoryReplicaAsync(
                inviteeCat.Id,
                AlignFrom(inviteeCat, inviteeCat.ParentId!.Value, inviteeUsuarioId, familyKeyPending, true),
                ct);
        }
    }

    private async Task PropagateAcceptedAsync(Guid sourceCategoryId, CancellationToken ct)
    {
        var source = await RequireAsync(sourceCategoryId, ct);
        if (source.IsSystem || source.IsPendingReview || source.Level != CategoryLevel.Analytical)
            return;

        var memberIds = await MemberIdsAsync(ct);
        if (memberIds.Count < 2)
            return;

        var familyKey = source.FamilyKey ?? source.Id;
        if (source.FamilyKey is null)
        {
            source.AssignFamilyIdentity(source.CreatedByUsuarioId ?? RequireUserId(), familyKey);
            await _uow.SaveChangesAsync(ct);
        }

        var all = await _repo.ListGroupCategoriesAsync(memberIds, ct);
        var active = all.Where(c => c.DeletedAt is null).ToList();
        var byId = active.ToDictionary(c => c.Id);
        var parentCode = ParentCode(source, byId);
        if (string.IsNullOrEmpty(parentCode))
            return;

        var createdBy = source.CreatedByUsuarioId ?? source.UsuarioId;
        await _repo.SetCategoryPendingReviewAsync(
            active.Where(c => c.FamilyKey == familyKey).Select(c => c.Id).Append(source.Id).Distinct().ToList(),
            false,
            ct);

        foreach (var memberId in memberIds.Where(id => id != source.UsuarioId))
        {
            await UpsertReplicaAsync(
                all,
                active,
                memberId,
                parentCode,
                source.Name,
                source.DisplayNumber,
                source.Section,
                source.IsActive,
                source.SortOrder,
                createdBy,
                familyKey,
                isPending: false,
                ct);
            await RenumberForUsuarioAsync(memberId, ct);
        }

        await RenumberAsync(ct);
    }

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
    public Task<CategoryResponse> UpdateSharingAsync(Guid id, UpdateCategorySharingRequest request, CancellationToken ct)
        => throw new ForbiddenException("Categorias não usam mais escopo Pessoal. O plano é compartilhado.");

    private async Task<bool> InUseAsync(IReadOnlyList<Guid> categoryIds, IReadOnlyList<Guid> memberIds, CancellationToken ct)
    {
        if (categoryIds.Count == 0)
            return false;

        if (memberIds.Count < 2)
        {
            return await _repo.AnyAsync<Entry>(e => e.CategoryId != null && categoryIds.Contains(e.CategoryId.Value), ct)
                   || await _repo.AnyAsync<CardPurchase>(p => categoryIds.Contains(p.CategoryId), ct)
                   || await _repo.AnyAsync<BudgetLine>(l => categoryIds.Contains(l.CategoryId), ct)
                   || await _repo.AnyAsync<PatrimonyItem>(p => categoryIds.Contains(p.CategoryId), ct)
                   || await _repo.AnyAsync<LifeProject>(p => p.CategoryId != null && categoryIds.Contains(p.CategoryId.Value), ct);
        }

        return await _repo.AnyForUsuariosAsync<Entry>(
                   memberIds, e => e.CategoryId != null && categoryIds.Contains(e.CategoryId.Value), ct)
               || await _repo.AnyForUsuariosAsync<CardPurchase>(memberIds, p => categoryIds.Contains(p.CategoryId), ct)
               || await _repo.AnyForUsuariosAsync<BudgetLine>(memberIds, l => categoryIds.Contains(l.CategoryId), ct)
               || await _repo.AnyForUsuariosAsync<PatrimonyItem>(memberIds, p => categoryIds.Contains(p.CategoryId), ct)
               || await _repo.AnyForUsuariosAsync<LifeProject>(memberIds, p => p.CategoryId != null && categoryIds.Contains(p.CategoryId.Value), ct);
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
