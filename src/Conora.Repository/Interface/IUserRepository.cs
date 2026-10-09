using Conora.Domain.Entities;

namespace Conora.Repository.Interface;

public interface IUserRepository : IBaseRepository<User>
{
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
    Task<User?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> FindByCpfAsync(string cpf, CancellationToken ct = default);
    Task<User?> FindByGoogleIdAsync(string googleId, CancellationToken ct = default);
    Task<IReadOnlyList<User>> ListExpiredDeletionsAsync(DateTime cutoffUtc, CancellationToken ct = default);
    void Remove(User user);
}
