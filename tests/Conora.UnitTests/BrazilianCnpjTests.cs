using Conora.Domain;
using Xunit;

namespace Conora.UnitTests;

public class BrazilianCnpjTests
{
    [Theory]
    [InlineData("04.252.011/0001-10", "04252011000110")]
    [InlineData("04252011000110", "04252011000110")]
    public void TryNormalize_accepts_valid_cnpj(string input, string expected)
    {
        Assert.True(BrazilianCnpj.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("11.111.111/1111-11")]
    [InlineData("123")]
    [InlineData("04252011000111")]
    [InlineData("")]
    public void TryNormalize_rejects_invalid_cnpj(string input)
    {
        Assert.False(BrazilianCnpj.TryNormalize(input, out _));
    }

    [Fact]
    public void TryExtract_finds_cnpj_in_ocr_text()
    {
        const string text = "CNPJ: 04.252.011/0001-10\nTOTAL R$ 12,90";
        Assert.True(BrazilianCnpj.TryExtract(text, out var normalized));
        Assert.Equal("04252011000110", normalized);
    }
}
