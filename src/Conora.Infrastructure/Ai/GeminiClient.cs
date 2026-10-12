using System.Net.Http.Json;
using System.Text.Json;
using Conora.Domain.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Conora.Infrastructure.Ai;

/// <summary>Server-side Gemini REST client. Without <c>Gemini:ApiKey</c> it reports itself as not configured.</summary>
public sealed class GeminiClient : IGeminiClient
{
    private const string DefaultModel = "gemini-2.0-flash";

    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly ILogger<GeminiClient> _logger;

    public GeminiClient(HttpClient http, IConfiguration configuration, ILogger<GeminiClient> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = configuration["Gemini:ApiKey"];
        _model = configuration["Gemini:Model"] is { Length: > 0 } model ? model : DefaultModel;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public Task<string?> GenerateJsonAsync(string systemInstruction, string userPrompt, CancellationToken ct)
    {
        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
            generationConfig = new { responseMimeType = "application/json", temperature = 0.3 }
        };
        return SendAsync(body, ct);
    }

    public Task<string?> GenerateJsonFromImageAsync(
        string systemInstruction,
        string userPrompt,
        ReadOnlyMemory<byte> imageBytes,
        string mimeType,
        CancellationToken ct)
    {
        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = userPrompt },
                        new
                        {
                            inline_data = new
                            {
                                mime_type = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType,
                                data = Convert.ToBase64String(imageBytes.Span)
                            }
                        }
                    }
                }
            },
            generationConfig = new { responseMimeType = "application/json", temperature = 0.1 }
        };
        return SendAsync(body, ct);
    }

    private async Task<string?> SendAsync(object body, CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(_model)}:generateContent")
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Add("x-goog-api-key", _apiKey);

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gemini respondeu {StatusCode}", (int)response.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            _logger.LogWarning(ex, "Falha ao chamar o Gemini");
            return null;
        }
    }
}
