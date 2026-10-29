using Conora.Domain.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Conora.Infrastructure.Storage;

public sealed class LocalUserPhotoStore : IUserPhotoStore
{
    private readonly string _root;
    private readonly ILogger<LocalUserPhotoStore> _logger;

    public LocalUserPhotoStore(IConfiguration configuration, ILogger<LocalUserPhotoStore> logger)
    {
        _logger = logger;
        var configured = configuration["Users:PhotoStoragePath"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "conora-user-photos")
            : configured;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct = default)
    {
        var ext = contentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? ".png"
            : contentType.Contains("webp", StringComparison.OrdinalIgnoreCase) ? ".webp"
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
            _logger.LogWarning(ex, "Falha ao apagar foto de perfil {Key}", objectKey);
        }

        return Task.CompletedTask;
    }
}
