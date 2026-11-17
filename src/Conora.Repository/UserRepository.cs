using System.Data;
using Conora.Domain.Entities;
using Conora.Domain.Ports;
using Conora.Infrastructure.Persistence;
using Conora.Repository.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

    public async Task<IReadOnlyList<User>> SearchActiveAsync(string term, int take, CancellationToken ct = default)
    {
        var pattern = $"%{term.Trim()}%";
        return await Set.IgnoreQueryFilters()
            .Where(u => u.DeletedAt == null
                        && (EF.Functions.ILike(u.Email, pattern) || EF.Functions.ILike(u.Name, pattern)))
            .OrderBy(u => u.Name)
            .Take(take)
            .ToListAsync(ct);
    }

    public Task<User?> FindActiveByIdAsync(Guid id, CancellationToken ct = default)
        => Set.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null, ct);

    public async Task LockPasswordResetAsync(Guid userId, CancellationToken ct = default)
    {
        var transaction = Context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Password reset lock requires an open transaction.");

        var connection = Context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(@key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.DbType = DbType.Int64;
        parameter.Value = PasswordResetLockKey(userId);
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(ct);
    }

    public Task ReloadAsync(User user, CancellationToken ct = default)
        => Context.Entry(user).ReloadAsync(ct);

    private static long PasswordResetLockKey(Guid userId)
    {
        Span<byte> bytes = stackalloc byte[16];
        userId.TryWriteBytes(bytes);
        var high = BitConverter.ToInt64(bytes[..8]);
        var low = BitConverter.ToInt64(bytes[8..]);
        return high ^ low;
    }
}
