using Conora.Domain.Entities;
using Conora.Infrastructure.Persistence;
using Conora.Repository.Interface;
using Microsoft.EntityFrameworkCore;

namespace Conora.Repository;

public sealed class UserRepository : BaseRepository<User>, IUserRepository
{
    public UserRepository(AppDbContext context) : base(context)
    {
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
        => Set.AnyAsync(u => u.Id == id, ct);
}
