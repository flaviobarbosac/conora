using System.Linq.Expressions;
using Conora.Domain.Entities;
using Conora.Domain.Enums;

namespace Conora.Repository.Interface;

/// <summary>
/// Thin generic data access for the finance domain. Tenant-scoped reads go through the
/// AppDbContext global filter. Cross-tenant reads require an explicit allow-list of UsuarioIds
/// (family peers). The only unscoped escape hatch is <see cref="FirstOrDefaultGlobalByKeyAsync{T}"/>
/// for inbound webhook routing by natural key.
/// </summary>
public interface IFinanceRepository
{
    /// <summary>Tracked lookup by id (tenant filtered).</summary>
    Task<T?> GetAsync<T>(Guid id, CancellationToken ct = default) where T : ModelBase, ITenantOwned;

    Task<T?> FirstOrDefaultAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default, bool track = true)
        where T : ModelBase, ITenantOwned;

    Task<List<T>> ListAsync<T>(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default, bool track = false)
        where T : ModelBase, ITenantOwned;

    /// <summary>Shapes a no-tracking query (filter, order, page, project) and materializes it.</summary>
    Task<List<TResult>> QueryAsync<T, TResult>(Func<IQueryable<T>, IQueryable<TResult>> shape, CancellationToken ct = default)
        where T : ModelBase, ITenantOwned;

    Task<int> CountAsync<T>(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
        where T : ModelBase, ITenantOwned;

    Task<bool> AnyAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        where T : ModelBase, ITenantOwned;

    Task<decimal> SumAsync<T>(Expression<Func<T, bool>> predicate, Expression<Func<T, decimal>> selector, CancellationToken ct = default)
        where T : ModelBase, ITenantOwned;

    /// <summary>
    /// Cross-tenant lookup restricted to <paramref name="allowedUsuarioIds"/> (non-empty).
    /// Soft-deleted rows stay excluded.
    /// </summary>
    Task<T?> FirstOrDefaultForUsuariosAsync<T>(
        IReadOnlyCollection<Guid> allowedUsuarioIds,
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default)
        where T : ModelBase, ITenantOwned;

    /// <summary>
    /// Cross-tenant list restricted to <paramref name="allowedUsuarioIds"/> (non-empty).
    /// Soft-deleted rows stay excluded.
    /// </summary>
    Task<List<T>> ListForUsuariosAsync<T>(
        IReadOnlyCollection<Guid> allowedUsuarioIds,
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default,
        bool track = false)
        where T : ModelBase, ITenantOwned;

    /// <summary>
    /// Cross-tenant shaped query. The base query already intersects
    /// <paramref name="allowedUsuarioIds"/>; soft-deleted rows stay excluded.
    /// </summary>
    Task<List<TResult>> QueryForUsuariosAsync<T, TResult>(
        IReadOnlyCollection<Guid> allowedUsuarioIds,
        Func<IQueryable<T>, IQueryable<TResult>> shape,
        CancellationToken ct = default)
        where T : ModelBase, ITenantOwned;

    /// <summary>
    /// Unscoped lookup by natural key for inbound webhooks only (e.g. WhatsApp phone).
    /// Never use for authenticated API paths.
    /// </summary>
    Task<T?> FirstOrDefaultGlobalByKeyAsync<T>(Expression<Func<T, bool>> naturalKeyPredicate, CancellationToken ct = default)
        where T : ModelBase, ITenantOwned;

    void Add<T>(T entity) where T : ModelBase, ITenantOwned;

    void AddRange<T>(IEnumerable<T> entities) where T : ModelBase, ITenantOwned;

    void SoftDelete<T>(T entity) where T : ModelBase, ITenantOwned;

    /// <summary>
    /// Privileged family-join write: sets analytical categories' SharingScope for a peer
    /// without going through tenant StampTenant (ExecuteUpdate).
    /// </summary>
    Task<int> SetCategorySharingForUsuarioAsync(Guid usuarioId, SharingScope scope, CancellationToken ct = default);

    /// <summary>
    /// Privileged family-join write: sets LifeProject.Scope for a peer via ExecuteUpdate.
    /// </summary>
    Task<int> SetLifeProjectScopeForUsuarioAsync(Guid usuarioId, LifeProjectScope scope, CancellationToken ct = default);

    /// <summary>
    /// Active and soft-deleted categories for the given users. Family chart sync only.
    /// </summary>
    Task<List<Category>> ListGroupCategoriesAsync(IReadOnlyCollection<Guid> usuarioIds, CancellationToken ct = default);

    Task<bool> AnyForUsuariosAsync<T>(
        IReadOnlyCollection<Guid> allowedUsuarioIds,
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default)
        where T : ModelBase, ITenantOwned;

    /// <summary>Inserts a chart replica on another member. Bypasses tenant stamp.</summary>
    Task InsertCategoryReplicaAsync(CategoryReplicaInsert row, CancellationToken ct = default);

    Task AlignCategoryReplicaAsync(Guid categoryId, CategoryReplicaAlign row, CancellationToken ct = default);

    Task RestoreCategoryReplicaAsync(Guid categoryId, CategoryReplicaAlign row, CancellationToken ct = default);

    Task SoftDeleteFamilyReplicasAsync(
        Guid familyKey,
        IReadOnlyCollection<Guid> usuarioIds,
        Guid? exceptCategoryId,
        CancellationToken ct = default);

    Task SetCategoryPendingReviewAsync(
        IReadOnlyCollection<Guid> categoryIds,
        bool isPendingReview,
        CancellationToken ct = default);

    Task SetCategoryDisplayNumberAsync(Guid categoryId, string? displayNumber, CancellationToken ct = default);

    /// <summary>Hands family categories created by <paramref name="fromUsuarioId"/> to <paramref name="toUsuarioId"/> on the copies that stay in the group.</summary>
    Task ReassignCategoryCreatorAsync(Guid fromUsuarioId, Guid toUsuarioId, CancellationToken ct = default);
}

public sealed record CategoryReplicaInsert(
    Guid Id,
    Guid UsuarioId,
    Guid ParentId,
    string Name,
    string? DisplayNumber,
    CategorySection Section,
    bool IsActive,
    int SortOrder,
    Guid CreatedByUsuarioId,
    Guid FamilyKey,
    bool IsPendingReview);

public sealed record CategoryReplicaAlign(
    Guid ParentId,
    string Name,
    string? DisplayNumber,
    CategorySection Section,
    bool IsActive,
    int SortOrder,
    Guid CreatedByUsuarioId,
    Guid FamilyKey,
    bool IsPendingReview);
