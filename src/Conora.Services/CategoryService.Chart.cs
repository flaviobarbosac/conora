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
public async Task EnsureDefaultsAsync(CancellationToken ct)
    {
        var existing = await _repo.ListAsync<Category>(c => c.IsSystem, ct, track: true);
        // Signup creates only the user row. Chart is seeded after onboarding.
        if (existing.Count == 0)
        {
            var userId = _tenant.UsuarioId;
            if (userId is null)
                return;
            var user = await _family.FindUserByIdAsync(userId.Value, ct);
            if (user is null || !user.OnboardingCompleted)
                return;
        }

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

        if (byCode.TryGetValue("ESS_DEBT", out var debtHead))
        {
            var custom = await _repo.ListAsync<Category>(c => !c.IsSystem, ct, track: true);
            var inDebt = new HashSet<Guid> { debtHead.Id };
            var pending = new Queue<Guid>();
            pending.Enqueue(debtHead.Id);
            while (pending.Count > 0)
            {
                var id = pending.Dequeue();
                foreach (var child in custom.Where(c => c.DeletedAt is null && c.ParentId == id))
                {
                    if (inDebt.Add(child.Id))
                        pending.Enqueue(child.Id);
                    if (child.Section != CategorySection.Debt)
                    {
                        child.Reparent(id, CategorySection.Debt);
                        changed = true;
                    }
                }
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
}
