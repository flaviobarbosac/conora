using Conora.Domain.Entities;
using Conora.Domain.Ports;
using Conora.Infrastructure.Persistence;
using Conora.Repository.Interface;
using Microsoft.EntityFrameworkCore;

namespace Conora.Repository;

public sealed class UserRepository : BaseRepository<User>, IUserRepository
{
    public UserRepository(AppDbContext context, ITenantContext tenant) : base(context, tenant)
    {
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
        => Set.AnyAsync(u => u.Id == id, ct);

    public Task<User?> FindByEmailAsync(string email, CancellationToken ct = default)
        => Set.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email && u.DeletedAt == null, ct);

    public Task<User?> FindByCpfAsync(string cpf, CancellationToken ct = default)
        => Set.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Cpf == cpf && u.DeletedAt == null, ct);

    public Task<User?> FindByGoogleIdAsync(string googleId, CancellationToken ct = default)
        => Set.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.GoogleId == googleId && u.DeletedAt == null, ct);

    public Task<User?> FindByAppleIdAsync(string appleId, CancellationToken ct = default)
        => Set.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.AppleId == appleId && u.DeletedAt == null, ct);

    public Task<User?> FindByPasswordResetHashAsync(string tokenHash, CancellationToken ct = default)
        => Set.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.PasswordResetTokenHash == tokenHash && u.DeletedAt == null, ct);

    public Task<User?> FindByEmailVerificationHashAsync(string tokenHash, CancellationToken ct = default)
        => Set.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.EmailVerificationTokenHash == tokenHash && u.DeletedAt == null, ct);

    public async Task<IReadOnlyList<Guid>> ListActiveUserIdsAsync(CancellationToken ct = default)
        => await Set.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.DeletedAt == null)
            .Select(u => u.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<User>> ListExpiredDeletionsAsync(DateTime cutoffUtc, CancellationToken ct = default)
        => await Set.IgnoreQueryFilters()
            .Where(u => u.DeletedAt != null && u.DeletedAt < cutoffUtc)
            .ToListAsync(ct);

    public void Remove(User user) => Set.Remove(user);
}
