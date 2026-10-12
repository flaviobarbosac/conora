using Conora.Domain.Enums;
using Conora.Domain.Ports;
using Microsoft.EntityFrameworkCore;

namespace Conora.Infrastructure.Persistence;

public sealed class EfReceiptOcrQueue : IReceiptOcrQueue
{
    private readonly AppDbContext _db;

    public EfReceiptOcrQueue(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<(Guid Id, Guid UsuarioId)>> ListProcessingAsync(int take, CancellationToken ct = default)
    {
        var rows = await _db.ReceiptIntakes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.DeletedAt == null && i.Status == ReceiptIntakeStatus.Processing)
            .OrderBy(i => i.CreatedAt)
            .Take(take)
            .Select(i => new { i.Id, i.UsuarioId })
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, r.UsuarioId)).ToList();
    }

    public async Task<IReadOnlyList<(Guid Id, Guid UsuarioId, string ImageObjectKey)>> ListExpiredImagesAsync(
        int take,
        CancellationToken ct = default)
    {
        var rows = await _db.ReceiptIntakes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.DeletedAt == null
                        && i.ImageExpiresAt < DateTime.UtcNow
                        && i.ImageObjectKey != "")
            .OrderBy(i => i.ImageExpiresAt)
            .Take(take)
            .Select(i => new { i.Id, i.UsuarioId, i.ImageObjectKey })
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, r.UsuarioId, r.ImageObjectKey)).ToList();
    }
}
