using System.Linq.Expressions;
using Conora.Domain.Entities;
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

    public Task<T?> FirstOrDefaultAnyTenantAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        where T : ModelBase, ITenantOwned
        => _context.Set<T>().IgnoreQueryFilters().Where(e => e.DeletedAt == null).FirstOrDefaultAsync(predicate, ct);

    public Task<List<T>> ListAnyTenantAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default, bool track = false)
        where T : ModelBase, ITenantOwned
    {
        IQueryable<T> query = _context.Set<T>().IgnoreQueryFilters().Where(e => e.DeletedAt == null);
        if (!track)
            query = query.AsNoTracking();
        return query.Where(predicate).ToListAsync(ct);
    }

    public Task<List<TResult>> QueryAnyTenantAsync<T, TResult>(Func<IQueryable<T>, IQueryable<TResult>> shape, CancellationToken ct = default)
        where T : ModelBase, ITenantOwned
        => shape(_context.Set<T>().IgnoreQueryFilters().AsNoTracking().Where(e => e.DeletedAt == null)).ToListAsync(ct);

    public void Add<T>(T entity) where T : ModelBase, ITenantOwned => _context.Set<T>().Add(entity);

    public void AddRange<T>(IEnumerable<T> entities) where T : ModelBase, ITenantOwned => _context.Set<T>().AddRange(entities);

    public void SoftDelete<T>(T entity) where T : ModelBase, ITenantOwned
    {
        if (entity.DeletedAt is not null)
            return;

        entity.DeletedAt = DateTime.UtcNow;
        entity.Version += 1;
    }
}
