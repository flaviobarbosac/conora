using System.Linq.Expressions;
using Conora.Domain.Entities;

namespace Conora.Repository.Interface;

public interface IBaseRepository<T> where T : ModelBase
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<T?> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<IReadOnlyList<T>> FindAllAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Add(T entity);
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}
