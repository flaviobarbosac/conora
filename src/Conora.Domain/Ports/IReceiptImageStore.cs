namespace Conora.Domain.Ports;

public interface IReceiptImageStore
{
    Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct = default);
    Task<byte[]?> ReadAsync(string objectKey, CancellationToken ct = default);
    Task DeleteAsync(string objectKey, CancellationToken ct = default);
    Task<int> DeleteExpiredAsync(IEnumerable<string> objectKeys, CancellationToken ct = default);
}
