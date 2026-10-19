using Conora.Domain.Enums;
using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class IanInsightTests
{
    [Fact]
    public void LeakQuestion_isAnInsight()
    {
        Assert.True(IanInsight.LooksLike("para onde minha receita está vazando?"));
        Assert.Equal(1, IanInsight.MonthCount("para onde minha receita está vazando?"));
    }

    [Fact]
    public void SixMonths_readsTheCount()
    {
        Assert.Equal(6, IanInsight.MonthCount("analise dos últimos 6 meses"));
        Assert.Equal(6, IanInsight.MonthCount("analise dos últimos seis meses"));
    }

    [Fact]
    public void OpeningRaioX_isNotAnInsight()
    {
        Assert.False(IanInsight.LooksLike("abre o raio-x"));
    }

    [Fact]
    public void Leak_namesTheLargestOutflow()
    {
        var text = IanInsight.DescribeLeak(
        [
            new IanInsight.MonthSlice(10000, 500, 200, 4000, 800),
            new IanInsight.MonthSlice(8000, 100, 0, 1500, 200),
        ]);

        Assert.Contains("Nos últimos 2 meses", text);
        Assert.Contains("R$ 18.000,00", text);
        Assert.Contains("Essencial, R$ 5.500,00", text);
        Assert.Contains("Social", text);
    }

    [Fact]
    public void EssentialDetail_namesTheLargestAccount()
    {
        Assert.Equal(CategorySection.Essential, IanInsight.Focus("aprofunda a análise do essencial"));
        var text = IanInsight.DescribeSection(
            CategorySection.Essential,
            [
                new IanInsight.AccountSlice("Moradia", 1000),
                new IanInsight.AccountSlice("Mercado", 3200),
            ],
            6);

        Assert.Contains("Nos últimos 6 meses", text);
        Assert.Contains("Essencial", text);
        Assert.Contains("Mercado, R$ 3.200,00", text);
        Assert.Contains("Moradia", text);
    }

    [Fact]
    public void EmptySection_saysThereIsNoAmount()
    {
        var text = IanInsight.DescribeSection(CategorySection.Essential, [], 1);
        Assert.Contains("Neste mês", text);
        Assert.Contains("não há valor em Essencial", text);
    }
}
