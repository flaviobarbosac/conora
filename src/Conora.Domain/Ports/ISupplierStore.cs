using Conora.Domain.Entities;

namespace Conora.Domain.Ports;

public interface ISupplierStore
{
    Task<Supplier?> FindByCnpjAsync(string cnpj, CancellationToken ct = default);
    Task<Supplier?> GetAsync(Guid id, CancellationToken ct = default);
    void Add(Supplier supplier);
}
