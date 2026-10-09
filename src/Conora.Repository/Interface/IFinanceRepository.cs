using System.Linq.Expressions;
using Conora.Domain.Entities;

namespace Conora.Repository.Interface;

/// <summary>
/// Thin generic data access for the finance domain. All reads go through the AppDbContext
/// global filter (tenant + soft delete), so callers can never see another tenant's rows.
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

    /// <summary>Cross-tenant lookup. Only for pre-authentication flows (e.g. WhatsApp webhook phone match).</summary>
    Task<T?> FirstOrDefaultAnyTenantAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        where T : ModelBase, ITenantOwned;

    /// <summary>Cross-tenant list. Soft-deleted rows stay excluded. Used for family group reads.</summary>
    Task<List<T>> ListAnyTenantAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default, bool track = false)
        where T : ModelBase, ITenantOwned;

    /// <summary>Cross-tenant shaped query. Soft-deleted rows stay excluded.</summary>
    Task<List<TResult>> QueryAnyTenantAsync<T, TResult>(Func<IQueryable<T>, IQueryable<TResult>> shape, CancellationToken ct = default)
        where T : ModelBase, ITenantOwned;

    void Add<T>(T entity) where T : ModelBase, ITenantOwned;

    void AddRange<T>(IEnumerable<T> entities) where T : ModelBase, ITenantOwned;

    void SoftDelete<T>(T entity) where T : ModelBase, ITenantOwned;
}
