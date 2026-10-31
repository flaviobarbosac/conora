using Conora.Domain.Entities;
using Conora.Infrastructure.Persistence;
using Conora.Repository.Interface;
using Microsoft.EntityFrameworkCore;

namespace Conora.Repository;

public sealed class PreRegistrationRepository : IPreRegistrationRepository
{
    private readonly AppDbContext _db;

    public PreRegistrationRepository(AppDbContext db) => _db = db;

    public void Add(PreRegistration entity) => _db.PreRegistrations.Add(entity);

    public void Remove(PreRegistration entity) => _db.PreRegistrations.Remove(entity);

    public Task<PreRegistration?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default)
        => _db.PreRegistrations.FirstOrDefaultAsync(e => e.TokenHash == tokenHash, ct);

    public Task<PreRegistration?> FindByEmailAsync(string email, CancellationToken ct = default)
        => _db.PreRegistrations.FirstOrDefaultAsync(e => e.Email == email, ct);

    public Task<PreRegistration?> FindByGoogleIdAsync(string googleId, CancellationToken ct = default)
        => _db.PreRegistrations.FirstOrDefaultAsync(e => e.GoogleId == googleId, ct);

    public Task<PreRegistration?> FindByAppleIdAsync(string appleId, CancellationToken ct = default)
        => _db.PreRegistrations.FirstOrDefaultAsync(e => e.AppleId == appleId, ct);

    public async Task<int> DeleteExpiredAsync(DateTime utcNow, CancellationToken ct = default)
    {
        var reuseCutoff = utcNow.AddMinutes(-1);
        return await _db.PreRegistrations
            .Where(e =>
                e.ExpiresAtUtc < utcNow
                || (e.FirstConsumedAtUtc != null && e.FirstConsumedAtUtc < reuseCutoff))
            .ExecuteDeleteAsync(ct);
    }
}
