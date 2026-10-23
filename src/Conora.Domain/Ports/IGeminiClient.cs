namespace Conora.Domain.Ports;

public interface IGeminiClient
{
    /// <summary>False when no Gemini:ApiKey is configured; the app keeps working without AI.</summary>
    bool IsConfigured { get; }

    /// <summary>Calls Gemini (server side only) and returns the raw JSON text, or null on any failure.</summary>
    Task<string?> GenerateJsonAsync(string systemInstruction, string userPrompt, CancellationToken ct);

    /// <summary>Multimodal JSON extraction from an image (OCR). Returns null when not configured or on failure.</summary>
    Task<string?> GenerateJsonFromImageAsync(
        string systemInstruction,
        string userPrompt,
        ReadOnlyMemory<byte> imageBytes,
        string mimeType,
        CancellationToken ct);

    /// <summary>Spoken reply as a WAV clip, or null when speech is unavailable.</summary>
    /// <param name="style">Natural-language speaking style, e.g. the age the voice should sound.</param>
    Task<byte[]?> SpeakAsync(string text, string voiceName, string style, CancellationToken ct);
}
