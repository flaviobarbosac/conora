using Conora.Domain.Entities;

namespace Conora.Repository.Interface;

public interface ILgpdRequestRepository : IBaseRepository<LgpdRequest>
{
    /// <summary>Pending export requests across all tenants (worker overnight job).</summary>
    Task<IReadOnlyList<LgpdRequest>> ListPendingExportsAsync(int take, CancellationToken ct = default);

    /// <summary>Open export request for the current tenant, if any.</summary>
    Task<LgpdRequest?> FindPendingExportAsync(CancellationToken ct = default);

    /// <summary>Latest completed export still available on storage for the current tenant.</summary>
    Task<LgpdRequest?> FindReadyExportAsync(CancellationToken ct = default);

    /// <summary>Completed exports whose S3 object past ExpiresAtUtc (purge worker).</summary>
    Task<IReadOnlyList<LgpdRequest>> ListExpiredExportsAsync(int take, CancellationToken ct = default);

    /// <summary>Lookup by SHA-256 of the one-time download token (cross-tenant, download endpoint).</summary>
    Task<LgpdRequest?> FindByDownloadTokenHashAsync(string tokenHash, CancellationToken ct = default);
}
