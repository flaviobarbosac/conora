using Conora.Domain.Entities;
using Conora.Domain.Ports;
using Conora.Infrastructure.Persistence;
using Conora.Repository.Interface;
using Microsoft.EntityFrameworkCore;

namespace Conora.Repository;

public sealed class LgpdRequestRepository : BaseRepository<LgpdRequest>, ILgpdRequestRepository
{
    public LgpdRequestRepository(AppDbContext context, ITenantContext tenant) : base(context, tenant)
    {
    }

    public async Task<IReadOnlyList<LgpdRequest>> ListPendingExportsAsync(int take, CancellationToken ct = default)
        => await Set.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.DeletedAt == null
                        && r.Kind == "export"
                        && r.CompletedAtUtc == null)
            .OrderBy(r => r.RequestedAtUtc)
            .Take(take)
            .ToListAsync(ct);

    public Task<LgpdRequest?> FindPendingExportAsync(CancellationToken ct = default)
        => Set.AsNoTracking()
            .Where(r => r.Kind == "export" && r.CompletedAtUtc == null)
            .OrderByDescending(r => r.RequestedAtUtc)
            .FirstOrDefaultAsync(ct);

    public Task<LgpdRequest?> FindReadyExportAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return Set.AsNoTracking()
            .Where(r => r.Kind == "export"
                        && r.CompletedAtUtc != null
                        && r.ExportObjectKey != null
                        && r.ExportObjectKey != ""
                        && r.ExpiresAtUtc != null
                        && r.ExpiresAtUtc > now)
            .OrderByDescending(r => r.CompletedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<LgpdRequest>> ListExpiredExportsAsync(int take, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await Set.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.DeletedAt == null
                        && r.Kind == "export"
                        && r.ExportObjectKey != null
                        && r.ExportObjectKey != ""
                        && r.ExpiresAtUtc != null
                        && r.ExpiresAtUtc <= now)
            .OrderBy(r => r.ExpiresAtUtc)
            .Take(take)
            .ToListAsync(ct);
    }

    public Task<LgpdRequest?> FindByDownloadTokenHashAsync(string tokenHash, CancellationToken ct = default)
        => Set.IgnoreQueryFilters()
            .Where(r => r.DeletedAt == null
                        && r.Kind == "export"
                        && r.DownloadTokenHash == tokenHash)
            .FirstOrDefaultAsync(ct);
}
