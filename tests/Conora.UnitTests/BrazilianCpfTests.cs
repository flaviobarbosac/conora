using Conora.Domain;
using Xunit;

namespace Conora.UnitTests;

public class BrazilianCpfTests
{
    [Theory]
    [InlineData("529.982.247-25", "52998224725")]
    [InlineData("52998224725", "52998224725")]
    public void TryNormalize_accepts_valid_cpf(string input, string expected)
    {
        Assert.True(BrazilianCpf.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("111.111.111-11")]
    [InlineData("123")]
    [InlineData("52998224724")]
    [InlineData("")]
    public void TryNormalize_rejects_invalid_cpf(string input)
    {
        Assert.False(BrazilianCpf.TryNormalize(input, out _));
    }
}
