namespace Conora.Domain.Ports;

public interface IWhatsAppMessenger
{
    bool IsConfigured { get; }

    Task SendTextAsync(string phoneE164, string text, CancellationToken ct);

    /// <summary>Downloads media bytes from Meta Cloud API by media id. Null when not configured or on failure.</summary>
    Task<(byte[] Bytes, string MimeType)?> DownloadMediaAsync(string mediaId, CancellationToken ct);
}
