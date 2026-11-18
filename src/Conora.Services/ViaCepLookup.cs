using System.Text.Json;
using Conora.Domain.Exceptions;

namespace Conora.Services;

public interface ICepLookup
{
    Task<bool> ExistsAsync(string cep, CancellationToken ct);
}

public sealed class ViaCepLookup : ICepLookup
{
    private readonly HttpClient _http;

    public ViaCepLookup(HttpClient http) => _http = http;

    public async Task<bool> ExistsAsync(string cep, CancellationToken ct)
    {
        var digits = new string((cep ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length != 8)
            return false;

        try
        {
            using var response = await _http.GetAsync($"ws/{digits}/json/", ct);
            if (!response.IsSuccessStatusCode)
                return false;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("erro", out var erro)
                && erro.ValueKind is JsonValueKind.True or JsonValueKind.String)
                return false;
            return doc.RootElement.TryGetProperty("uf", out var uf)
                && uf.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(uf.GetString());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new ValidationException("cep", "Não foi possível validar o CEP. Tente de novo.");
        }
    }
}
