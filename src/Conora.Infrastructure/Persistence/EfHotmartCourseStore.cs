using Conora.Domain.Entities;
using Conora.Domain.Ports;
using Microsoft.EntityFrameworkCore;

namespace Conora.Infrastructure.Persistence;

public sealed class EfHotmartCourseStore : IHotmartCourseStore
{
    private readonly AppDbContext _db;

    public EfHotmartCourseStore(AppDbContext db)
    {
        _db = db;
    }

    public Task<HotmartCoursePurchase?> FindByTransactionAsync(string transaction, CancellationToken ct = default)
        => _db.HotmartCoursePurchases.FirstOrDefaultAsync(p => p.Transaction == transaction, ct);

    public Task<bool> HasApprovedAsync(string email, long productId, CancellationToken ct = default)
        => _db.HotmartCoursePurchases.AnyAsync(
            p => p.Email == email && p.Approved && p.RevokedAt == null && p.ProductId == productId,
            ct);

    public async Task<IReadOnlyList<HotmartCoursePurchase>> ListByEmailAsync(string email, long productId, CancellationToken ct = default)
        => await _db.HotmartCoursePurchases
            .Where(p => p.Email == email && p.ProductId == productId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<HotmartCoursePurchase>> ListAwaitingReleaseAsync(DateTime utcNow, CancellationToken ct = default)
        => await _db.HotmartCoursePurchases
            .Where(p => p.RevokedAt == null
                && p.UnlockAt != null
                && p.UnlockAt <= utcNow
                && (!p.Approved || p.ReleaseEmailSentAt == null))
            .ToListAsync(ct);

    public async Task AddAsync(HotmartCoursePurchase purchase, CancellationToken ct = default)
        => await _db.HotmartCoursePurchases.AddAsync(purchase, ct);

    public async Task<HotmartStudentPage> ListStudentsAsync(HotmartStudentQuery query, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 50 ? 20 : query.PageSize;
        var users = _db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.DeletedAt == null);

        var rows = _db.HotmartCoursePurchases.AsNoTracking()
            .Where(p => p.ProductId == query.ProductId)
            .Select(p => new StudentSource(
                p.Email,
                users.Where(u => u.Email == p.Email).Select(u => u.Name).FirstOrDefault(),
                users.Any(u => u.Email == p.Email),
                p.AcquiredAt,
                p.UnlockAt,
                p.Approved,
                p.RevokedAt));

        if (!string.IsNullOrWhiteSpace(query.Email))
        {
            var email = query.Email.Trim().ToLowerInvariant();
            rows = rows.Where(p => p.Email.Contains(email));
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            var name = query.Name.Trim().ToLowerInvariant();
            rows = rows.Where(p => p.Name != null && p.Name.ToLower().Contains(name));
        }

        if (query.AcquiredFrom is not null)
            rows = rows.Where(p => p.AcquiredAt >= query.AcquiredFrom);
        if (query.AcquiredTo is not null)
            rows = rows.Where(p => p.AcquiredAt <= query.AcquiredTo);
        if (query.ReleasedFrom is not null)
            rows = rows.Where(p => p.ReleasedAt != null && p.ReleasedAt >= query.ReleasedFrom);
        if (query.ReleasedTo is not null)
            rows = rows.Where(p => p.ReleasedAt != null && p.ReleasedAt <= query.ReleasedTo);

        rows = query.Status switch
        {
            "revoked" => rows.Where(p => p.RevokedAt != null),
            "pending" => rows.Where(p => p.RevokedAt == null && (!p.Approved || p.ReleasedAt == null || p.ReleasedAt > now)),
            "expired" => rows.Where(p => p.RevokedAt == null && p.Approved && p.ReleasedAt != null && p.ReleasedAt.Value.AddDays(90) <= now),
            "active" => rows.Where(p => p.RevokedAt == null && p.Approved && p.ReleasedAt != null && p.ReleasedAt <= now && p.ReleasedAt.Value.AddDays(90) > now),
            _ => rows
        };

        var ordered = rows
            .OrderBy(p => p.EmailMatches ? 1 : 0)
            .ThenBy(p => p.Name != null && p.Name != "" ? p.Name : p.Email)
            .ThenBy(p => p.Email);

        var total = await ordered.CountAsync(ct);
        var slice = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = slice.Select(p => new HotmartStudentRow(
            p.Email,
            string.IsNullOrWhiteSpace(p.Name) ? null : p.Name,
            p.EmailMatches,
            StatusOf(p, now),
            p.AcquiredAt,
            p.ReleasedAt,
            p.ReleasedAt?.AddDays(90))).ToList();

        return new HotmartStudentPage(items, page, pageSize, total);
    }

    private static string StatusOf(StudentSource row, DateTime now)
    {
        if (row.RevokedAt is not null)
            return "revoked";
        if (!row.Approved || row.ReleasedAt is null || row.ReleasedAt > now)
            return "pending";
        if (row.ReleasedAt.Value.AddDays(90) <= now)
            return "expired";
        return "active";
    }

    private sealed record StudentSource(
        string Email,
        string? Name,
        bool EmailMatches,
        DateTime AcquiredAt,
        DateTime? ReleasedAt,
        bool Approved,
        DateTime? RevokedAt);
}
