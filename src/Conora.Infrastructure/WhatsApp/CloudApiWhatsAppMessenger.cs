using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Conora.Domain.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Conora.Infrastructure.WhatsApp;

/// <summary>Sends text replies through the Meta WhatsApp Cloud API when token and phone number id are set.</summary>
public sealed class CloudApiWhatsAppMessenger : IWhatsAppMessenger
{
    private readonly HttpClient _http;
    private readonly string? _token;
    private readonly string? _phoneNumberId;
    private readonly ILogger<CloudApiWhatsAppMessenger> _logger;

    public CloudApiWhatsAppMessenger(HttpClient http, IConfiguration configuration, ILogger<CloudApiWhatsAppMessenger> logger)
    {
        _http = http;
        _logger = logger;
        _token = configuration["WhatsApp:AccessToken"];
        _phoneNumberId = configuration["WhatsApp:PhoneNumberId"];
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_token) && !string.IsNullOrWhiteSpace(_phoneNumberId);

    public async Task SendTextAsync(string phoneE164, string text, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            _logger.LogInformation("WhatsApp outbound skipped (not configured) for {Phone}: {Text}", phoneE164, text);
            return;
        }

        var to = Regex.Replace(phoneE164, @"\D", "");
        if (string.IsNullOrWhiteSpace(to))
            return;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_phoneNumberId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Content = JsonContent.Create(new
        {
            messaging_product = "whatsapp",
            to,
            type = "text",
            text = new { body = text }
        });

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("WhatsApp send failed for {Phone}: {Status} {Body}", phoneE164, (int)response.StatusCode, body);
        }
    }

    public async Task<(byte[] Bytes, string MimeType)?> DownloadMediaAsync(string mediaId, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(mediaId))
            return null;

        try
        {
            using var metaRequest = new HttpRequestMessage(HttpMethod.Get, mediaId);
            metaRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            using var metaResponse = await _http.SendAsync(metaRequest, ct);
            if (!metaResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("WhatsApp media meta failed: {Status}", (int)metaResponse.StatusCode);
                return null;
            }

            using var metaDoc = await JsonDocument.ParseAsync(await metaResponse.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var url = metaDoc.RootElement.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
            var mime = metaDoc.RootElement.TryGetProperty("mime_type", out var mimeEl) ? mimeEl.GetString() : "image/jpeg";
            if (string.IsNullOrWhiteSpace(url))
                return null;

            using var fileRequest = new HttpRequestMessage(HttpMethod.Get, url);
            fileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            using var fileResponse = await _http.SendAsync(fileRequest, ct);
            if (!fileResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("WhatsApp media download failed: {Status}", (int)fileResponse.StatusCode);
                return null;
            }

            var bytes = await fileResponse.Content.ReadAsByteArrayAsync(ct);
            return (bytes, mime ?? "image/jpeg");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "WhatsApp media download error for {MediaId}", mediaId);
            return null;
        }
    }
}
