using Conora.Domain.Entities;
using Conora.Domain.Ports;
using Conora.Infrastructure.Persistence;
using Conora.Repository.Interface;
using Microsoft.EntityFrameworkCore;

namespace Conora.Repository;

public sealed class RefreshTokenRepository : BaseRepository<RefreshToken>, IRefreshTokenRepository
{
    public RefreshTokenRepository(AppDbContext context, ITenantContext tenant) : base(context, tenant)
    {
    }

    public Task<RefreshToken?> FindActiveByHashAsync(string tokenHash, CancellationToken ct = default)
        => Set.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.RevokedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow, ct);

    public async Task RevokeAllForUserAsync(CancellationToken ct = default)
    {
        var tokens = await Set.Where(t => t.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var token in tokens)
            token.Revoke();
    }
}
