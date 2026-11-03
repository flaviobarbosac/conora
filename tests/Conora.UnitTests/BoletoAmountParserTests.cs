using Conora.Domain;
using Xunit;

namespace Conora.UnitTests;

public class BoletoAmountParserTests
{
    [Fact]
    public void Bank_line_reads_the_last_ten_digits_as_reais()
    {
        const string line = "34191.79001 01043.510047 91020.150008 1 84660000020000";
        Assert.Equal(200.00m, BoletoAmountParser.TryParse(line));
    }

    [Fact]
    public void Barcode_44_reads_the_value_field()
    {
        var barcode = "341917900" + "0000000200" + "0000000000000000000000000";
        Assert.Equal(44, barcode.Length);
        Assert.Equal(2.00m, BoletoAmountParser.TryParse(barcode));
    }

    [Fact]
    public void Utility_48_reads_the_value_when_the_third_digit_is_6_or_7()
    {
        var digits = "826" + "1" + "00000020000" + new string('0', 48 - 15);
        Assert.Equal(48, digits.Length);
        Assert.Equal(200.00m, BoletoAmountParser.TryParse(digits));
    }

    [Fact]
    public void Zero_value_is_an_open_amount()
    {
        const string line = "34191.79001 01043.510047 91020.150008 1 84660000000000";
        Assert.Null(BoletoAmountParser.TryParse(line));
    }
}
