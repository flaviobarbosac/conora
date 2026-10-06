using Conora.Domain.Entities;

namespace Conora.Repository.Interface;

public interface IRefreshTokenRepository : IBaseRepository<RefreshToken>
{
    Task<RefreshToken?> FindActiveByHashAsync(string tokenHash, CancellationToken ct = default);
    Task RevokeAllForUserAsync(CancellationToken ct = default);
}
