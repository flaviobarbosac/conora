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
}
