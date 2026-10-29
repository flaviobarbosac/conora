using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Conora.Domain.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Conora.Infrastructure.Storage;

/// <summary>S3-backed LGPD export storage. Used when LgpdExport:S3:Bucket is set.</summary>
public sealed class S3LgpdExportStore : ILgpdExportStore, IDisposable
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly string _prefix;
    private readonly ILogger<S3LgpdExportStore> _logger;

    public S3LgpdExportStore(IConfiguration configuration, ILogger<S3LgpdExportStore> logger)
    {
        _logger = logger;
        _bucket = configuration["LgpdExport:S3:Bucket"]
            ?? throw new InvalidOperationException("LgpdExport:S3:Bucket não configurado.");
        _prefix = (configuration["LgpdExport:S3:Prefix"] ?? "lgpd-exports").Trim().Trim('/');
        var region = configuration["LgpdExport:S3:Region"] ?? "us-east-1";
        _s3 = new AmazonS3Client(RegionEndpoint.GetBySystemName(region));
    }

    public async Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var safe = string.IsNullOrWhiteSpace(fileName) ? "export.xlsx" : Path.GetFileName(fileName);
        var key = $"{_prefix}/{DateTime.UtcNow:yyyy/MM/dd}/{Guid.CreateVersion7():N}-{safe}";
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
        };
        await _s3.PutObjectAsync(request, ct);
        return key;
    }

    public async Task<byte[]?> ReadAsync(string objectKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey)) return null;
        try
        {
            using var response = await _s3.GetObjectAsync(_bucket, objectKey, ct);
            await using var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey)) return;
        try
        {
            await _s3.DeleteObjectAsync(_bucket, objectKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao apagar exportação LGPD no S3 {Key}", objectKey);
        }
    }

    public void Dispose() => _s3.Dispose();
}
