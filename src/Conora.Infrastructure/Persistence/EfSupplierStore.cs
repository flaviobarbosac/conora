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

    public Task<Supplier?> FindByNormalizedNameAsync(string normalizedName, CancellationToken ct = default)
        => _db.Suppliers.FirstOrDefaultAsync(s => s.NormalizedName == normalizedName, ct);

    public void Add(Supplier supplier) => _db.Suppliers.Add(supplier);

    public async Task<string?> GetUserCategoryCodeAsync(Guid supplierId, Guid usuarioId, CancellationToken ct = default)
    {
        var row = await _db.Set<SupplierUserCategory>()
            .FirstOrDefaultAsync(r => r.SupplierId == supplierId && r.UsuarioId == usuarioId, ct);
        return row?.CategoryCode;
    }

    public async Task UpsertUserCategoryAsync(Guid supplierId, Guid usuarioId, string categoryCode, CancellationToken ct = default)
    {
        var row = await _db.Set<SupplierUserCategory>()
            .FirstOrDefaultAsync(r => r.SupplierId == supplierId && r.UsuarioId == usuarioId, ct);
        if (row is null)
        {
            _db.Set<SupplierUserCategory>().Add(SupplierUserCategory.Create(supplierId, usuarioId, categoryCode));
            return;
        }

        row.SetCategoryCode(categoryCode);
    }

    public async Task<Guid?> GetUserBankAccountIdAsync(Guid supplierId, Guid usuarioId, CancellationToken ct = default)
    {
        var row = await _db.Set<SupplierUserBankAccount>()
            .FirstOrDefaultAsync(r => r.SupplierId == supplierId && r.UsuarioId == usuarioId, ct);
        return row?.AccountId;
    }

    public async Task UpsertUserBankAccountAsync(Guid supplierId, Guid usuarioId, Guid accountId, CancellationToken ct = default)
    {
        var row = await _db.Set<SupplierUserBankAccount>()
            .FirstOrDefaultAsync(r => r.SupplierId == supplierId && r.UsuarioId == usuarioId, ct);
        if (row is null)
        {
            _db.Set<SupplierUserBankAccount>().Add(SupplierUserBankAccount.Create(supplierId, usuarioId, accountId));
            return;
        }

        row.SetAccountId(accountId);
    }

    public async Task<IReadOnlyList<Supplier>> FindSimilarByCnpjAsync(string cnpjDigits, int take, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cnpjDigits) || cnpjDigits.Length < 8)
            return [];

        var prefix = cnpjDigits[..8];
        return await _db.Suppliers
            .Where(s => s.Cnpj != null && s.Cnpj.StartsWith(prefix) && s.Cnpj != cnpjDigits)
            .OrderBy(s => s.Cnpj)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Supplier>> FindSimilarByNameAsync(string name, int take, CancellationToken ct = default)
    {
        var normalized = Supplier.NormalizeName(name);
        if (normalized.Length < 4)
            return [];

        var needle = normalized.Length > 12 ? normalized[..12] : normalized;
        return await _db.Suppliers
            .Where(s => s.NormalizedName.Contains(needle))
            .OrderBy(s => s.NormalizedName)
            .Take(take)
            .ToListAsync(ct);
    }
}
