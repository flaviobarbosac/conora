using System.Globalization;
using System.Text;
using System.Text.Json;
using Conora.Domain.Catalog;
using Conora.Domain.Enums;
using Conora.Domain.Entities;
using Conora.Infrastructure.Maps;
using Conora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Conora.Services;

public sealed record ConsoleGeoUser(
    Guid Id,
    string Nome,
    string? NomeFantasia,
    string? Email,
    string? Telefone,
    string? Uf,
    string? Municipio,
    string? Bairro,
    string? Logradouro,
    string? Numero,
    string? Cep,
    string? Categoria,
    string? Plano,
    double? Latitude,
    double? Longitude);

public sealed record ConsoleGeoBairro(string Nome, int Quantidade);

public sealed record ConsoleGeoMunicipio(string Nome, int Quantidade, IReadOnlyList<ConsoleGeoBairro> Bairros);

public sealed record ConsoleGeoEstado(string Uf, string Nome, int Quantidade, IReadOnlyList<ConsoleGeoMunicipio> Municipios);

public sealed record ConsoleGeoMap(
    int Total,
    int ComCoordenada,
    IReadOnlyList<ConsoleGeoUser> Usuarios,
    IReadOnlyList<ConsoleGeoEstado> Estados);

public sealed record ConsoleGeoConfig(
    string PinLabelCampo,
    string? PinLabelTexto,
    bool MostrarRotulo,
    IReadOnlyList<string> CamposVisiveis,
    string? GoogleMapsApiKey);

public sealed record ConsoleGeoConfigSave(
    string? PinLabelCampo,
    string? PinLabelTexto,
    bool MostrarRotulo,
    IReadOnlyList<string>? CamposVisiveis);

public sealed record ConsoleGeoCoordenada(Guid UsuarioId, double Latitude, double Longitude);

public static class ConsoleGeoCampos
{
    public const string PinLabelPadrao = "nome";

    public static readonly string[] Todos =
    [
        "nome", "nomeFantasia", "email", "telefone", "municipio", "uf", "bairro",
        "logradouro", "numero", "cep", "categoria", "plano"
    ];

    public static readonly string[] Padrao = ["nome", "municipio", "uf", "bairro", "email", "plano"];

    public static bool EhValido(string? campo)
        => !string.IsNullOrWhiteSpace(campo) && Todos.Contains(campo.Trim(), StringComparer.Ordinal);
}

public static class ConsoleGeoAgrupamento
{
    private static readonly Dictionary<string, string> UfNomes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AC"] = "Acre",
        ["AL"] = "Alagoas",
        ["AP"] = "Amapá",
        ["AM"] = "Amazonas",
        ["BA"] = "Bahia",
        ["CE"] = "Ceará",
        ["DF"] = "Distrito Federal",
        ["ES"] = "Espírito Santo",
        ["GO"] = "Goiás",
        ["MA"] = "Maranhão",
        ["MT"] = "Mato Grosso",
        ["MS"] = "Mato Grosso do Sul",
        ["MG"] = "Minas Gerais",
        ["PA"] = "Pará",
        ["PB"] = "Paraíba",
        ["PR"] = "Paraná",
        ["PE"] = "Pernambuco",
        ["PI"] = "Piauí",
        ["RJ"] = "Rio de Janeiro",
        ["RN"] = "Rio Grande do Norte",
        ["RS"] = "Rio Grande do Sul",
        ["RO"] = "Rondônia",
        ["RR"] = "Roraima",
        ["SC"] = "Santa Catarina",
        ["SP"] = "São Paulo",
        ["SE"] = "Sergipe",
        ["TO"] = "Tocantins",
    };

    private static readonly Dictionary<string, string> NomeParaUf = UfNomes
        .ToDictionary(par => SemAcento(par.Value), par => par.Key, StringComparer.OrdinalIgnoreCase);

    public static bool CoordenadaValida(double? latitude, double? longitude)
        => latitude is >= -90 and <= 90
           && longitude is >= -180 and <= 180
           && !(latitude == 0 && longitude == 0);

    public static ConsoleGeoMap Montar(IReadOnlyList<ConsoleGeoUser> usuarios)
    {
        var ordenados = usuarios
            .OrderBy(u => u.Nome, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var estados = ordenados
            .GroupBy(u => NormalizarUf(u.Uf))
            .Select(g =>
            {
                var municipios = g
                    .GroupBy(u => Chave(u.Municipio))
                    .Select(mg =>
                    {
                        var bairros = mg
                            .GroupBy(u => Chave(u.Bairro))
                            .Select(bg => new ConsoleGeoBairro(Exibir(bg.Select(u => u.Bairro)), bg.Count()))
                            .OrderByDescending(b => b.Quantidade)
                            .ThenBy(b => b.Nome, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        return new ConsoleGeoMunicipio(Exibir(mg.Select(u => u.Municipio)), mg.Count(), bairros);
                    })
                    .OrderByDescending(m => m.Quantidade)
                    .ThenBy(m => m.Nome, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var uf = g.Key;
                var nome = uf.Length == 2 && UfNomes.TryGetValue(uf, out var conhecido) ? conhecido : uf;
                return new ConsoleGeoEstado(uf, nome, g.Count(), municipios);
            })
            .Where(e => e.Uf.Length == 2)
            .OrderByDescending(e => e.Quantidade)
            .ThenBy(e => e.Nome, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ConsoleGeoMap(
            ordenados.Count,
            ordenados.Count(u => u.Latitude is not null && u.Longitude is not null),
            ordenados,
            estados);
    }

    public static string NormalizarUf(string? uf)
    {
        var texto = uf?.Trim() ?? string.Empty;
        if (texto.Length == 0)
            return string.Empty;
        var upper = texto.ToUpperInvariant();
        if (upper.Length == 2 && UfNomes.ContainsKey(upper))
            return upper;
        return NomeParaUf.TryGetValue(SemAcento(texto), out var sigla) ? sigla : string.Empty;
    }

    private static string SemAcento(string texto)
    {
        var form = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(form.Length);
        foreach (var c in form)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string Chave(string? valor)
    {
        var texto = valor?.Trim();
        return string.IsNullOrEmpty(texto) ? string.Empty : texto.ToUpperInvariant();
    }

    private static string Exibir(IEnumerable<string?> valores)
    {
        var escolhido = valores
            .Select(v => v?.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .Cast<string>()
            .OrderByDescending(v => v.Any(char.IsLower))
            .ThenByDescending(v => v.Length)
            .FirstOrDefault();
        return escolhido ?? string.Empty;
    }
}

public sealed class ConsoleGeoService
{
    private const int LoteMaximo = 200;
    private const double QuaseIgualLimite = 0.00001;

    private readonly AppDbContext _db;
    private readonly string? _mapsApiKey;

    public ConsoleGeoService(AppDbContext db, IOptions<GoogleMapsOptions> maps)
    {
        _db = db;
        _mapsApiKey = string.IsNullOrWhiteSpace(maps.Value.ApiKey) ? null : maps.Value.ApiKey.Trim();
    }

    public async Task<ConsoleGeoMap> ObterAsync(CancellationToken ct)
    {
        var agora = DateTime.UtcNow;
        var usuarios = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.DeletedAt == null)
            .Select(u => new
            {
                u.Id,
                u.Name,
                u.Email,
                u.Phone,
                u.Cep,
                u.Logradouro,
                u.Numero,
                u.Bairro,
                u.Municipio,
                u.Uf,
                u.Latitude,
                u.Longitude
            })
            .ToListAsync(ct);

        var assinaturas = await _db.WorkspaceSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.DeletedAt == null)
            .Select(s => new { s.UsuarioId, s.Plan, s.Status, s.ExpiresAt })
            .ToListAsync(ct);

        var planoPorUsuario = assinaturas
            .Where(s => s.Status == SubscriptionStatus.Active && s.ExpiresAt > agora)
            .GroupBy(s => s.UsuarioId)
            .ToDictionary(g => g.Key, g => PlanCatalog.DisplayTitle(g.First().Plan));

        var linhas = usuarios
            .Select(u => new ConsoleGeoUser(
                u.Id,
                string.IsNullOrWhiteSpace(u.Name) ? u.Email : u.Name,
                null,
                u.Email,
                u.Phone,
                u.Uf,
                u.Municipio,
                u.Bairro,
                u.Logradouro,
                u.Numero,
                u.Cep,
                null,
                planoPorUsuario.GetValueOrDefault(u.Id),
                u.Latitude,
                u.Longitude))
            .ToList();

        return ConsoleGeoAgrupamento.Montar(linhas);
    }

    public async Task<ConsoleGeoConfig> ObterConfigAsync(CancellationToken ct)
    {
        var config = await ConfigAtualAsync(ct);
        return config is null ? Padrao() : Map(config);
    }

    public async Task<ConsoleGeoConfig> SalvarConfigAsync(ConsoleGeoConfigSave pedido, CancellationToken ct)
    {
        var config = await ConfigAtualAsync(ct);
        if (config is null)
        {
            config = new ConsoleMapConfig();
            _db.ConsoleMapConfigs.Add(config);
        }

        config.PinLabelField = ConsoleGeoCampos.EhValido(pedido.PinLabelCampo)
            ? pedido.PinLabelCampo!.Trim()
            : ConsoleGeoCampos.PinLabelPadrao;
        config.PinLabelText = string.IsNullOrWhiteSpace(pedido.PinLabelTexto) ? null : pedido.PinLabelTexto.Trim();
        config.ShowLabel = pedido.MostrarRotulo;
        config.VisibleFieldsJson = JsonSerializer.Serialize(NormalizarCampos(pedido.CamposVisiveis));
        await _db.SaveChangesAsync(ct);
        return Map(config);
    }

    public async Task<int> SalvarCoordenadasAsync(IReadOnlyList<ConsoleGeoCoordenada> itens, CancellationToken ct)
    {
        if (itens.Count == 0)
            return 0;

        var validos = itens
            .Where(i => ConsoleGeoAgrupamento.CoordenadaValida(i.Latitude, i.Longitude))
            .GroupBy(i => i.UsuarioId)
            .Select(g => g.Last())
            .Take(LoteMaximo)
            .ToList();
        if (validos.Count == 0)
            return 0;

        var ids = validos.Select(i => i.UsuarioId).ToList();
        var usuarios = await _db.Users.IgnoreQueryFilters()
            .Where(u => ids.Contains(u.Id) && u.DeletedAt == null)
            .ToListAsync(ct);

        var porId = validos.ToDictionary(i => i.UsuarioId);
        var alterados = 0;
        foreach (var usuario in usuarios)
        {
            var coord = porId[usuario.Id];
            if (QuaseIgual(usuario.Latitude, coord.Latitude) && QuaseIgual(usuario.Longitude, coord.Longitude))
                continue;

            usuario.SetCoordinates(coord.Latitude, coord.Longitude);
            alterados++;
        }

        if (alterados > 0)
            await _db.SaveChangesAsync(ct);
        return alterados;
    }

    private async Task<ConsoleMapConfig?> ConfigAtualAsync(CancellationToken ct)
        => await _db.ConsoleMapConfigs
            .Where(c => c.DeletedAt == null)
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync(ct);

    private ConsoleGeoConfig Padrao()
        => new(ConsoleGeoCampos.PinLabelPadrao, null, true, ConsoleGeoCampos.Padrao, _mapsApiKey);

    private ConsoleGeoConfig Map(ConsoleMapConfig config)
        => new(
            ConsoleGeoCampos.EhValido(config.PinLabelField) ? config.PinLabelField : ConsoleGeoCampos.PinLabelPadrao,
            config.PinLabelText,
            config.ShowLabel,
            LerCampos(config.VisibleFieldsJson),
            _mapsApiKey);

    private static IReadOnlyList<string> NormalizarCampos(IReadOnlyList<string>? campos)
        => (campos ?? [])
            .Where(ConsoleGeoCampos.EhValido)
            .Select(c => c.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<string> LerCampos(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ConsoleGeoCampos.Padrao;
        try
        {
            var lista = JsonSerializer.Deserialize<List<string>>(json);
            return lista is null ? ConsoleGeoCampos.Padrao : NormalizarCampos(lista);
        }
        catch (JsonException)
        {
            return ConsoleGeoCampos.Padrao;
        }
    }

    private static bool QuaseIgual(double? atual, double novo)
        => atual is not null && Math.Abs(atual.Value - novo) < QuaseIgualLimite;
}
