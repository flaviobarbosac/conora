using System.Net.Http.Headers;
using System.Net.Http.Json;
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
}
