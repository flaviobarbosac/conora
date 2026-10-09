using Conora.Domain.Entities;
using Conora.Domain.Ports;
using Conora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Conora.Infrastructure.Email;

public sealed class EfEmailSuppressionStore : IEmailSuppressionStore
{
    private readonly AppDbContext _db;

    public EfEmailSuppressionStore(AppDbContext db) => _db = db;

    public Task<bool> IsSuppressedAsync(string email, CancellationToken ct = default)
    {
        var normalized = EmailSuppression.Normalize(email);
        if (string.IsNullOrEmpty(normalized))
            return Task.FromResult(false);

        return _db.EmailSuppressions.AsNoTracking().AnyAsync(e => e.Email == normalized, ct);
    }

    public async Task UpsertAsync(string email, string reason, string? sourceMessageId, CancellationToken ct = default)
    {
        var entity = EmailSuppression.Create(email, reason, sourceMessageId);
        var existing = await _db.EmailSuppressions.FirstOrDefaultAsync(e => e.Email == entity.Email, ct);
        if (existing is null)
        {
            _db.EmailSuppressions.Add(entity);
        }
        else
        {
            existing.Reason = entity.Reason;
            if (!string.IsNullOrEmpty(sourceMessageId))
                existing.SourceMessageId = sourceMessageId;
        }

        await _db.SaveChangesAsync(ct);
    }
}
