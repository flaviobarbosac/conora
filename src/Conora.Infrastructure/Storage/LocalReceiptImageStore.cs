using Conora.Domain.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Conora.Infrastructure.Storage;

public sealed class LocalReceiptImageStore : IReceiptImageStore
{
    private readonly string _root;
    private readonly ILogger<LocalReceiptImageStore> _logger;

    public LocalReceiptImageStore(IConfiguration configuration, ILogger<LocalReceiptImageStore> logger)
    {
        _logger = logger;
        var configured = configuration["Receipts:StoragePath"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "conora-receipts")
            : configured;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct = default)
    {
        var ext = contentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? ".png"
            : contentType.Contains("webp", StringComparison.OrdinalIgnoreCase) ? ".webp"
            : contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ? ".pdf"
            : ".jpg";
        var key = $"{DateTime.UtcNow:yyyy/MM/dd}/{Guid.CreateVersion7():N}{ext}";
        var path = Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
        return key;
    }

    public async Task<byte[]?> ReadAsync(string objectKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
            return null;
        var path = Path.Combine(_root, objectKey.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            return null;
        return await File.ReadAllBytesAsync(path, ct);
    }

    public Task DeleteAsync(string objectKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
            return Task.CompletedTask;
        var path = Path.Combine(_root, objectKey.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao apagar imagem de recibo {Key}", objectKey);
        }

        return Task.CompletedTask;
    }

    public async Task<int> DeleteExpiredAsync(IEnumerable<string> objectKeys, CancellationToken ct = default)
    {
        var count = 0;
        foreach (var key in objectKeys)
        {
            await DeleteAsync(key, ct);
            count++;
        }

        return count;
    }
}
