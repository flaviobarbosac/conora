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

    public async Task<CategoryResponse> SetActiveAsync(Guid id, SetCategoryActiveRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        await EnsureCanManageChartAsync(ct);
        var account = await RequireAsync(id, ct);
        if (account.IsPendingReview)
            throw new ValidationException("id", "Aceite ou recuse a categoria antes de editá-la.");

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
}
