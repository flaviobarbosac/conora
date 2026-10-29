using Conora.Domain.Entities;

namespace Conora.Repository.Interface;

public interface IPreRegistrationRepository
{
    void Add(PreRegistration entity);
    void Remove(PreRegistration entity);
    Task<PreRegistration?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task<PreRegistration?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<PreRegistration?> FindByGoogleIdAsync(string googleId, CancellationToken ct = default);
    Task<PreRegistration?> FindByAppleIdAsync(string appleId, CancellationToken ct = default);
    Task<int> DeleteExpiredAsync(DateTime utcNow, CancellationToken ct = default);
}
