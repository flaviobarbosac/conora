using Conora.Domain.Entities;

namespace Conora.Domain.Ports;

public interface ISupplierStore
{
    Task<Supplier?> FindByCnpjAsync(string cnpj, CancellationToken ct = default);
    Task<Supplier?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Supplier?> FindByNormalizedNameAsync(string normalizedName, CancellationToken ct = default);
    void Add(Supplier supplier);
    Task<string?> GetUserCategoryCodeAsync(Guid supplierId, Guid usuarioId, CancellationToken ct = default);
    Task UpsertUserCategoryAsync(Guid supplierId, Guid usuarioId, string categoryCode, CancellationToken ct = default);
    Task<Guid?> GetUserBankAccountIdAsync(Guid supplierId, Guid usuarioId, CancellationToken ct = default);
    Task UpsertUserBankAccountAsync(Guid supplierId, Guid usuarioId, Guid accountId, CancellationToken ct = default);
    Task<IReadOnlyList<Supplier>> FindSimilarByCnpjAsync(string cnpjDigits, int take, CancellationToken ct = default);
    Task<IReadOnlyList<Supplier>> FindSimilarByNameAsync(string name, int take, CancellationToken ct = default);
}
