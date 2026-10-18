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

    public async Task<byte[]?> SpeakAsync(string text, string voiceName, CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        var spoken = text.Trim();
        if (spoken.Length == 0)
            return null;
        if (spoken.Length > 400)
            spoken = spoken[..400];

        try
        {
            var wav = await RequestSpeechAsync(
                "gemini-3.8-flash-tts",
                SpeechBody(spoken, voiceName, wavModel: true),
                ct);
            wav ??= await RequestSpeechAsync(
                "gemini-2.5-flash-preview-tts",
                SpeechBody("Fale em português do Brasil, com voz humana e calma: " + spoken, voiceName, wavModel: false),
                ct);
            return wav;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            _logger.LogWarning(ex, "Falha ao gerar a voz");
            return null;
        }
    }

    private static object SpeechBody(string text, string voiceName, bool wavModel)
    {
        if (wavModel)
        {
            return new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new
                            {
                                text,
                                speech_metadata = new { style = "voz humana, calma e natural, português do Brasil" }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    responseModalities = new[] { "AUDIO" },
                    speechConfig = new { voiceConfig = new { voice = voiceName } }
                }
            };
        }

        return new
        {
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text } } }
            },
            generationConfig = new
            {
                responseModalities = new[] { "AUDIO" },
                speechConfig = new { voiceConfig = new { prebuiltVoiceConfig = new { voiceName } } }
            }
        };
    }

    private async Task<byte[]?> RequestSpeechAsync(string model, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("x-goog-api-key", _apiKey);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Gemini voz {Model} respondeu {StatusCode}", model, (int)response.StatusCode);
            return null;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var parts = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");
        foreach (var part in parts.EnumerateArray())
        {
            if (!part.TryGetProperty("inlineData", out var inline) && !part.TryGetProperty("inline_data", out inline))
                continue;
            var data = inline.GetProperty("data").GetString();
            if (string.IsNullOrWhiteSpace(data))
                continue;
            var bytes = Convert.FromBase64String(data);
            if (bytes.Length > 12
                && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F')
                return bytes;

            var rate = 24000;
            if (inline.TryGetProperty("mimeType", out var mime) || inline.TryGetProperty("mime_type", out mime))
                rate = ReadSampleRate(mime.GetString()) ?? rate;
            return Wav(bytes, rate);
        }

        return null;
    }

    private static int? ReadSampleRate(string? mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
            return null;
        var marker = "rate=";
        var index = mime.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;
        var raw = mime[(index + marker.Length)..];
        var end = raw.IndexOfAny([';', ' ']);
        if (end > 0)
            raw = raw[..end];
        return int.TryParse(raw, out var rate) && rate > 0 ? rate : null;
    }

    private static byte[] Wav(byte[] pcm, int sampleRate)
    {
        var dataLength = pcm.Length;
        var header = new byte[44];
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
        BitConverter.GetBytes(36 + dataLength).CopyTo(header, 4);
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(header, 8);
        System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(header, 12);
        BitConverter.GetBytes(16).CopyTo(header, 16);
        BitConverter.GetBytes((short)1).CopyTo(header, 20);
        BitConverter.GetBytes((short)1).CopyTo(header, 22);
        BitConverter.GetBytes(sampleRate).CopyTo(header, 24);
        BitConverter.GetBytes(sampleRate * 2).CopyTo(header, 28);
        BitConverter.GetBytes((short)2).CopyTo(header, 32);
        BitConverter.GetBytes((short)16).CopyTo(header, 34);
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(header, 36);
        BitConverter.GetBytes(dataLength).CopyTo(header, 40);
        var wav = new byte[header.Length + pcm.Length];
        header.CopyTo(wav, 0);
        pcm.CopyTo(wav, header.Length);
        return wav;
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
