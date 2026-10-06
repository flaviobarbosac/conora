using Conora.Domain.Entities;

namespace Conora.Repository.Interface;

public interface IUserRepository : IBaseRepository<User>
{
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}
