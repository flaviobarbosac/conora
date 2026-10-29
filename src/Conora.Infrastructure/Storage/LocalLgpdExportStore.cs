using Conora.Domain.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Conora.Infrastructure.Storage;

/// <summary>Dev/local stand-in for S3 LGPD export objects.</summary>
public sealed class LocalLgpdExportStore : ILgpdExportStore
{
    private readonly string _root;
    private readonly ILogger<LocalLgpdExportStore> _logger;

    public LocalLgpdExportStore(IConfiguration configuration, ILogger<LocalLgpdExportStore> logger)
    {
        _logger = logger;
        var configured = configuration["LgpdExport:StoragePath"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "conora-lgpd-exports")
            : configured;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var safe = string.IsNullOrWhiteSpace(fileName) ? "export.xlsx" : Path.GetFileName(fileName);
        var key = $"lgpd/{DateTime.UtcNow:yyyy/MM/dd}/{Guid.CreateVersion7():N}-{safe}";
        var path = Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
        return key;
    }

    public async Task<byte[]?> ReadAsync(string objectKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey)) return null;
        var path = Path.Combine(_root, objectKey.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return null;
        return await File.ReadAllBytesAsync(path, ct);
    }

    public Task DeleteAsync(string objectKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey)) return Task.CompletedTask;
        var path = Path.Combine(_root, objectKey.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao apagar exportação LGPD {Key}", objectKey);
        }

        return Task.CompletedTask;
    }
}
