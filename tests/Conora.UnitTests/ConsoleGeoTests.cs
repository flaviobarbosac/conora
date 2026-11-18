using Conora.Domain.Entities;
using Conora.Services;
using Xunit;

namespace Conora.UnitTests;

public class ConsoleGeoTests
{
    [Fact]
    public void Montar_junta_sigla_e_nome_da_uf_e_deixa_uf_vazia_fora_da_lista()
    {
        var mapa = ConsoleGeoAgrupamento.Montar(
        [
            Usuario("Ana", "SP", "São Paulo", "Centro", -23.5, -46.6),
            Usuario("Bia", "São Paulo", "são paulo", null, -23.6, -46.7),
            Usuario("Caio", null, null, null, 0, 0),
            Usuario("Duda", "Marte", "X", "Y", -10, -40),
        ]);

        Assert.Equal(4, mapa.Total);
        Assert.Equal(4, mapa.ComCoordenada);
        Assert.Single(mapa.Estados);
        Assert.Equal("SP", mapa.Estados[0].Uf);
        Assert.Equal(2, mapa.Estados[0].Quantidade);
        Assert.Equal("São Paulo", mapa.Estados[0].Municipios[0].Nome);
        Assert.Equal(2, mapa.Estados[0].Municipios[0].Quantidade);
        Assert.Contains(mapa.Estados[0].Municipios[0].Bairros, b => b.Nome == "Centro");
        Assert.Contains(mapa.Estados[0].Municipios[0].Bairros, b => b.Nome == "");
    }

    [Fact]
    public void Montar_ordena_por_quantidade_e_prefere_o_texto_com_minuscula()
    {
        var mapa = ConsoleGeoAgrupamento.Montar(
        [
            Usuario("A", "RJ", "NITEROI", "Centro", -22, -43),
            Usuario("B", "MG", "Belo Horizonte", "CENTRO", -19, -43),
            Usuario("C", "MG", "BELO HORIZONTE", "centro histórico", -19.1, -43.1),
            Usuario("D", "RJ", "Niterói", "Centro", -22.1, -43.1),
            Usuario("E", "RJ", "Niterói", "Centro", -22.2, -43.2),
        ]);

        Assert.Equal("RJ", mapa.Estados[0].Uf);
        Assert.Equal("MG", mapa.Estados[1].Uf);
        Assert.Equal("Niterói", mapa.Estados[0].Municipios[0].Nome);
        Assert.Contains(mapa.Estados[1].Municipios[0].Bairros, b => b.Nome == "centro histórico");
    }

    [Fact]
    public void Coordenada_zero_zero_nao_e_pin()
    {
        Assert.False(ConsoleGeoAgrupamento.CoordenadaValida(0, 0));
        Assert.False(ConsoleGeoAgrupamento.CoordenadaValida(91, 0));
        Assert.True(ConsoleGeoAgrupamento.CoordenadaValida(-14.235, -51.925));
    }

    [Fact]
    public void Troca_de_cep_e_exclusao_zeram_coordenada()
    {
        var user = User.Create("Ana", "ana@exemplo.com");
        user.UpdateContact(null, "01310100", "Avenida Paulista", "S/N", "Bela Vista", "São Paulo", "SP");
        user.SetCoordinates(-23.5, -46.6);
        user.UpdateContact("11999999999", "01310100", "Avenida Paulista", "S/N", "Bela Vista", "São Paulo", "SP");
        Assert.Equal(-23.5, user.Latitude);

        user.UpdateContact(null, "22041080", "Rua Barata Ribeiro", "10", "Copacabana", "Rio de Janeiro", "RJ");
        Assert.Null(user.Latitude);
        Assert.Null(user.Longitude);

        user.SetCoordinates(-22.9, -43.1);
        user.RequestDeletion();
        Assert.Null(user.Latitude);
        Assert.Null(user.Longitude);
        Assert.NotNull(user.DeletedAt);
    }

    [Fact]
    public void Numero_vazio_vira_s_n_e_troca_de_rua_zera_coordenada()
    {
        var user = User.Create("Ana", "ana@exemplo.com");
        user.UpdateContact(null, "01310100", "Avenida Paulista", "  ", "Bela Vista", "São Paulo", "sp");
        Assert.Equal("S/N", user.Numero);
        Assert.Equal("SP", user.Uf);
        user.SetCoordinates(-23.5, -46.6);
        user.UpdateContact(null, "01310100", "Rua Augusta", "S/N", "Bela Vista", "São Paulo", "SP");
        Assert.Null(user.Latitude);
    }

    [Fact]
    public void SetCoordinates_recusa_zero_zero()
    {
        var user = User.Create("Ana", "ana@exemplo.com");
        user.SetCoordinates(0, 0);
        Assert.Null(user.Latitude);
    }

    private static ConsoleGeoUser Usuario(
        string nome, string? uf, string? municipio, string? bairro, double? lat, double? lng)
        => new(Guid.NewGuid(), nome, null, null, null, uf, municipio, bairro, null, null, null, null, null, lat, lng);
}
