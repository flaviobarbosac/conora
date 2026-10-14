using System.Linq.Expressions;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Infrastructure.Persistence;
using Conora.Repository.Interface;
using Microsoft.EntityFrameworkCore;

namespace Conora.Repository;

public sealed class FinanceRepository : IFinanceRepository
{
    private readonly AppDbContext _context;

    public FinanceRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<T?> GetAsync<T>(Guid id, CancellationToken ct = default) where T : ModelBase, ITenantOwned
        => _context.Set<T>().FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<T?> FirstOrDefaultAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default, bool track = true)
        where T : ModelBase, ITenantOwned
    {
        IQueryable<T> query = _context.Set<T>();
        if (!track)
            query = query.AsNoTracking();

        return query.FirstOrDefaultAsync(predicate, ct);
    }

    public Task<List<T>> ListAsync<T>(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default, bool track = false)
        where T : ModelBase, ITenantOwned
    {
        IQueryable<T> query = _context.Set<T>();
        if (!track)
            query = query.AsNoTracking();
        if (predicate is not null)
            query = query.Where(predicate);

        return query.ToListAsync(ct);
    }

    public Task<List<TResult>> QueryAsync<T, TResult>(Func<IQueryable<T>, IQueryable<TResult>> shape, CancellationToken ct = default)
        where T : ModelBase, ITenantOwned
        => shape(_context.Set<T>().AsNoTracking()).ToListAsync(ct);

    public Task<int> CountAsync<T>(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
        where T : ModelBase, ITenantOwned
        => predicate is null ? _context.Set<T>().CountAsync(ct) : _context.Set<T>().CountAsync(predicate, ct);

    public Task<bool> AnyAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        where T : ModelBase, ITenantOwned
        => _context.Set<T>().AnyAsync(predicate, ct);

    public Task<decimal> SumAsync<T>(Expression<Func<T, bool>> predicate, Expression<Func<T, decimal>> selector, CancellationToken ct = default)
        where T : ModelBase, ITenantOwned
        => _context.Set<T>().Where(predicate).SumAsync(selector, ct);

    public Task<T?> FirstOrDefaultForUsuariosAsync<T>(
        IReadOnlyCollection<Guid> allowedUsuarioIds,
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default)
        where T : ModelBase, ITenantOwned
        => ForUsuariosQuery<T>(allowedUsuarioIds).FirstOrDefaultAsync(predicate, ct);

    public Task<List<T>> ListForUsuariosAsync<T>(
        IReadOnlyCollection<Guid> allowedUsuarioIds,
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default,
        bool track = false)
        where T : ModelBase, ITenantOwned
    {
        IQueryable<T> query = ForUsuariosQuery<T>(allowedUsuarioIds, track);
        return query.Where(predicate).ToListAsync(ct);
    }

    public Task<List<TResult>> QueryForUsuariosAsync<T, TResult>(
        IReadOnlyCollection<Guid> allowedUsuarioIds,
        Func<IQueryable<T>, IQueryable<TResult>> shape,
        CancellationToken ct = default)
        where T : ModelBase, ITenantOwned
        => shape(ForUsuariosQuery<T>(allowedUsuarioIds, track: false)).ToListAsync(ct);

    public Task<T?> FirstOrDefaultGlobalByKeyAsync<T>(Expression<Func<T, bool>> naturalKeyPredicate, CancellationToken ct = default)
        where T : ModelBase, ITenantOwned
        => _context.Set<T>().IgnoreQueryFilters().Where(e => e.DeletedAt == null).FirstOrDefaultAsync(naturalKeyPredicate, ct);

    public void Add<T>(T entity) where T : ModelBase, ITenantOwned => _context.Set<T>().Add(entity);

    public void AddRange<T>(IEnumerable<T> entities) where T : ModelBase, ITenantOwned => _context.Set<T>().AddRange(entities);

    public void SoftDelete<T>(T entity) where T : ModelBase, ITenantOwned
    {
        if (entity.DeletedAt is not null)
            return;

        entity.DeletedAt = DateTime.UtcNow;
        entity.Version += 1;
    }

    public Task<int> SetCategorySharingForUsuarioAsync(Guid usuarioId, SharingScope scope, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return _context.Set<Category>().IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null
                        && c.UsuarioId == usuarioId
                        && c.Level == CategoryLevel.Analytical)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.SharingScope, scope)
                .SetProperty(c => c.UpdatedAt, now)
                .SetProperty(c => c.Version, c => c.Version + 1), ct);
    }

    public Task<int> SetLifeProjectScopeForUsuarioAsync(Guid usuarioId, LifeProjectScope scope, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return _context.Set<LifeProject>().IgnoreQueryFilters()
            .Where(p => p.DeletedAt == null && p.UsuarioId == usuarioId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Scope, scope)
                .SetProperty(p => p.UpdatedAt, now)
                .SetProperty(p => p.Version, p => p.Version + 1), ct);
    }

    public Task<List<Category>> ListGroupCategoriesAsync(IReadOnlyCollection<Guid> usuarioIds, CancellationToken ct = default)
    {
        EnsureAllowList(usuarioIds);
        return _context.Set<Category>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => usuarioIds.Contains(c.UsuarioId))
            .ToListAsync(ct);
    }

    public Task<bool> AnyForUsuariosAsync<T>(
        IReadOnlyCollection<Guid> allowedUsuarioIds,
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default)
        where T : ModelBase, ITenantOwned
        => ForUsuariosQuery<T>(allowedUsuarioIds, track: false).AnyAsync(predicate, ct);

    public async Task InsertCategoryReplicaAsync(CategoryReplicaInsert row, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var level = CategoryLevel.Analytical.ToString();
        var section = row.Section.ToString();
        var scope = SharingScope.Family.ToString();
        await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO categories (
                "Id", "UsuarioId", "ParentId", "Name", "Code", "DisplayNumber", "Level", "Section",
                "IsSystem", "IsActive", "SortOrder", "SharingScope", "CreatedByUsuarioId", "FamilyKey",
                "CreatedAt", "UpdatedAt", "DeletedAt", "Version")
            VALUES (
                {row.Id}, {row.UsuarioId}, {row.ParentId}, {row.Name}, {null}, {row.DisplayNumber}, {level}, {section},
                {false}, {row.IsActive}, {row.SortOrder}, {scope}, {row.CreatedByUsuarioId}, {row.FamilyKey},
                {now}, {now}, {null}, {1L})
            """, ct);
    }

    public async Task AlignCategoryReplicaAsync(Guid categoryId, CategoryReplicaAlign row, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await _context.Set<Category>().IgnoreQueryFilters()
            .Where(c => c.Id == categoryId && c.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.ParentId, row.ParentId)
                .SetProperty(c => c.Name, row.Name)
                .SetProperty(c => c.DisplayNumber, row.DisplayNumber)
                .SetProperty(c => c.Section, row.Section)
                .SetProperty(c => c.IsActive, row.IsActive)
                .SetProperty(c => c.SortOrder, row.SortOrder)
                .SetProperty(c => c.SharingScope, SharingScope.Family)
                .SetProperty(c => c.CreatedByUsuarioId, row.CreatedByUsuarioId)
                .SetProperty(c => c.FamilyKey, row.FamilyKey)
                .SetProperty(c => c.UpdatedAt, now)
                .SetProperty(c => c.Version, c => c.Version + 1), ct);
    }

    public async Task RestoreCategoryReplicaAsync(Guid categoryId, CategoryReplicaAlign row, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await _context.Set<Category>().IgnoreQueryFilters()
            .Where(c => c.Id == categoryId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.DeletedAt, (DateTime?)null)
                .SetProperty(c => c.ParentId, row.ParentId)
                .SetProperty(c => c.Name, row.Name)
                .SetProperty(c => c.DisplayNumber, row.DisplayNumber)
                .SetProperty(c => c.Section, row.Section)
                .SetProperty(c => c.IsActive, row.IsActive)
                .SetProperty(c => c.SortOrder, row.SortOrder)
                .SetProperty(c => c.SharingScope, SharingScope.Family)
                .SetProperty(c => c.CreatedByUsuarioId, row.CreatedByUsuarioId)
                .SetProperty(c => c.FamilyKey, row.FamilyKey)
                .SetProperty(c => c.UpdatedAt, now)
                .SetProperty(c => c.Version, c => c.Version + 1), ct);
    }

    public async Task SoftDeleteFamilyReplicasAsync(
        Guid familyKey,
        IReadOnlyCollection<Guid> usuarioIds,
        Guid exceptCategoryId,
        CancellationToken ct = default)
    {
        EnsureAllowList(usuarioIds);
        var now = DateTime.UtcNow;
        await _context.Set<Category>().IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null
                        && c.FamilyKey == familyKey
                        && usuarioIds.Contains(c.UsuarioId)
                        && c.Id != exceptCategoryId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.DeletedAt, now)
                .SetProperty(c => c.UpdatedAt, now)
                .SetProperty(c => c.Version, c => c.Version + 1), ct);
    }

    public async Task ReassignCategoryCreatorAsync(Guid fromUsuarioId, Guid toUsuarioId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await _context.Set<Category>().IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null
                        && c.CreatedByUsuarioId == fromUsuarioId
                        && c.UsuarioId != fromUsuarioId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.CreatedByUsuarioId, toUsuarioId)
                .SetProperty(c => c.UpdatedAt, now)
                .SetProperty(c => c.Version, c => c.Version + 1), ct);
    }

    private IQueryable<T> ForUsuariosQuery<T>(IReadOnlyCollection<Guid> allowedUsuarioIds, bool track = true)
        where T : ModelBase, ITenantOwned
    {
        EnsureAllowList(allowedUsuarioIds);
        IQueryable<T> query = _context.Set<T>().IgnoreQueryFilters()
            .Where(e => e.DeletedAt == null && allowedUsuarioIds.Contains(e.UsuarioId));
        if (!track)
            query = query.AsNoTracking();
        return query;
    }

    private static void EnsureAllowList(IReadOnlyCollection<Guid> allowedUsuarioIds)
    {
        if (allowedUsuarioIds is null || allowedUsuarioIds.Count == 0)
        {
            throw new ArgumentException(
                "Cross-tenant reads require a non-empty allow-list of UsuarioIds.",
                nameof(allowedUsuarioIds));
        }
    }
}
