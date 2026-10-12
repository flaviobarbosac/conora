using Conora.Domain.Entities;
using Conora.Domain.Ports;
using Microsoft.EntityFrameworkCore;

namespace Conora.Infrastructure.Persistence;

public sealed class EfSupplierStore : ISupplierStore
{
    private readonly AppDbContext _db;

    public EfSupplierStore(AppDbContext db) => _db = db;

    public Task<Supplier?> FindByCnpjAsync(string cnpj, CancellationToken ct = default)
        => _db.Suppliers.FirstOrDefaultAsync(s => s.Cnpj == cnpj, ct);

    public Task<Supplier?> GetAsync(Guid id, CancellationToken ct = default)
        => _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);

    public void Add(Supplier supplier) => _db.Suppliers.Add(supplier);
}
