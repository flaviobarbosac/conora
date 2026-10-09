using System.Linq.Expressions;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Ports;
using Conora.Infrastructure.Persistence;
using Conora.Repository.Interface;
using Microsoft.EntityFrameworkCore;

namespace Conora.Repository;

public class BaseRepository<T> : IBaseRepository<T> where T : ModelBase
{
    protected readonly AppDbContext Context;
    protected readonly DbSet<T> Set;
    private readonly ITenantContext _tenant;

    public BaseRepository(AppDbContext context, ITenantContext tenant)
    {
        Context = context;
        _tenant = tenant;
        Set = context.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await Set.FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<T?> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => Set.AsNoTracking().FirstOrDefaultAsync(predicate, ct);

    public async Task<IReadOnlyList<T>> FindAllAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => await Set.AsNoTracking().Where(predicate).ToListAsync(ct);

    public async Task AddAsync(T entity, CancellationToken ct = default)
    {
        StampTenant(entity);
        await Set.AddAsync(entity, ct);
    }

    public void Add(T entity)
    {
        StampTenant(entity);
        Set.Add(entity);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null || entity.DeletedAt is not null)
            return;

        entity.DeletedAt = DateTime.UtcNow;
        entity.Version += 1;
    }

    protected IQueryable<T> BuildQuery(ExcluidosFiltro excluidos)
    {
        IQueryable<T> query = Set.IgnoreQueryFilters();

        if (typeof(ITenantOwned).IsAssignableFrom(typeof(T)))
        {
            if (!_tenant.UsuarioId.HasValue)
                return query.Where(_ => false);

            var usuarioId = _tenant.UsuarioId.Value;
            query = query.Where(e => EF.Property<Guid>(e, nameof(ITenantOwned.UsuarioId)) == usuarioId);
        }
        else if (typeof(T) == typeof(User) && _tenant.UsuarioId.HasValue)
        {
            var usuarioId = _tenant.UsuarioId.Value;
            query = query.Where(e => e.Id == usuarioId);
        }
        else if (typeof(T) == typeof(User))
        {
            return query.Where(_ => false);
        }

        return excluidos switch
        {
            ExcluidosFiltro.Ativos => query.Where(e => e.DeletedAt == null),
            ExcluidosFiltro.Only => query.Where(e => e.DeletedAt != null),
            ExcluidosFiltro.Todos => query,
            _ => query.Where(e => e.DeletedAt == null)
        };
    }

    private void StampTenant(T entity)
    {
        if (entity is not ITenantOwned owned)
            return;

        if (!_tenant.UsuarioId.HasValue)
            throw new UnauthorizedAccessException("Tenant não autenticado para carimbar UsuarioId.");

        var tid = _tenant.UsuarioId.Value;
        if (owned.UsuarioId != Guid.Empty && owned.UsuarioId != tid)
            throw new UnauthorizedAccessException("Operação fora do tenant autenticado.");

        owned.UsuarioId = tid;
    }
}
