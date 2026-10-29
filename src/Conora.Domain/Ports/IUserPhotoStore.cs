namespace Conora.Domain.Ports;

/// <summary>Persistent object storage for profile photos (local folder in dev, same pattern as receipts).</summary>
public interface IUserPhotoStore
{
    Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct = default);
    Task<byte[]?> ReadAsync(string objectKey, CancellationToken ct = default);
    Task DeleteAsync(string objectKey, CancellationToken ct = default);
}
