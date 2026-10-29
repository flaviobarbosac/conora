namespace Conora.Domain.Entities;

public class LgpdRequest : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string Kind { get; private set; } = default!;
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>S3 object key for a completed export. Null until the overnight job finishes.</summary>
    public string? ExportObjectKey { get; private set; }

    /// <summary>UTC instant when the S3 object must be deleted (generation + 30 days).</summary>
    public DateTime? ExpiresAtUtc { get; private set; }

    /// <summary>SHA-256 of the one-time download token. Never store the raw token.</summary>
    public string? DownloadTokenHash { get; private set; }

    /// <summary>Set on first successful download; token becomes unusable.</summary>
    public DateTime? DownloadConsumedAtUtc { get; private set; }

    private LgpdRequest()
    {
    }

    public static LgpdRequest Export() => New("export");

    public static LgpdRequest Deletion() => New("deletion");

    public void Complete() => CompletedAtUtc = DateTime.UtcNow;

    /// <summary>Marks overnight export as ready on S3 with a one-time download token hash.</summary>
    public void MarkExportReady(string objectKey, DateTime expiresAtUtc, string downloadTokenHash)
    {
        if (Kind != "export")
            throw new InvalidOperationException("Só pedidos de exportação recebem arquivo.");
        if (string.IsNullOrWhiteSpace(objectKey))
            throw new ArgumentException("Object key é obrigatória.", nameof(objectKey));
        if (string.IsNullOrWhiteSpace(downloadTokenHash))
            throw new ArgumentException("Hash do token é obrigatório.", nameof(downloadTokenHash));

        ExportObjectKey = objectKey.Trim();
        ExpiresAtUtc = expiresAtUtc;
        DownloadTokenHash = downloadTokenHash.Trim();
        DownloadConsumedAtUtc = null;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public bool IsExportReadyOnStorage
        => Kind == "export"
           && CompletedAtUtc is not null
           && !string.IsNullOrWhiteSpace(ExportObjectKey)
           && ExpiresAtUtc is DateTime exp
           && exp > DateTime.UtcNow;

    public void ConsumeDownload() => DownloadConsumedAtUtc = DateTime.UtcNow;

    /// <summary>Clears storage metadata after S3 purge (keeps the completed request history).</summary>
    public void ClearExportStorage()
    {
        ExportObjectKey = null;
        DownloadTokenHash = null;
        ExpiresAtUtc = null;
    }

    private static LgpdRequest New(string kind) => new()
    {
        Kind = kind,
        RequestedAtUtc = DateTime.UtcNow
    };
}
