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
            p => p.Email == email && p.Approved && p.ProductId == productId,
            ct);

    public async Task AddAsync(HotmartCoursePurchase purchase, CancellationToken ct = default)
        => await _db.HotmartCoursePurchases.AddAsync(purchase, ct);
}
