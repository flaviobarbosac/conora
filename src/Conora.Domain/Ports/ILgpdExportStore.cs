namespace Conora.Domain.Ports;

/// <summary>Object storage for overnight LGPD Excel exports (S3 in prod, local folder in dev).</summary>
public interface ILgpdExportStore
{
    Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);

    Task<byte[]?> ReadAsync(string objectKey, CancellationToken ct = default);

    Task DeleteAsync(string objectKey, CancellationToken ct = default);
}
