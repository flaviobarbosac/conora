using Conora.Domain.Exceptions;
using Conora.Services.Common;
using Conora.Services.Contracts;
using Xunit;

namespace Conora.UnitTests;

public class ImportParserTests
{
    [Theory]
    [InlineData("01/02/2026")]
    [InlineData("2026-02-01")]
    [InlineData("20260201")]
    public void ParseDate_accepts_common_formats(string text)
    {
        var date = ImportParser.ParseDate(text);
        Assert.NotNull(date);
        Assert.Equal(2026, date!.Value.Year);
        Assert.Equal(2, date.Value.Month);
        Assert.Equal(1, date.Value.Day);
        Assert.Equal(DateTimeKind.Utc, date.Value.Kind);
    }

    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("-10,00", -10)]
    [InlineData("(10,00)", -10)]
    [InlineData("R$ 5,50", 5.5)]
    public void ParseAmount_handles_ptBR_and_signs(string text, double expected)
        => Assert.Equal((decimal)expected, ImportParser.ParseAmount(text));

    [Fact]
    public void ParseCsv_auto_maps_header_columns()
    {
        var csv = """
            Data;Valor;Descrição
            10/01/2026;100,00;Padaria
            11/01/2026;-50,50;Uber
            """;
        var rows = ImportParser.ParseCsv(csv, mapping: null);
        Assert.Equal(2, rows.Count);
        Assert.Equal(100m, rows[0].Amount);
        Assert.Equal("Padaria", rows[0].Description);
        Assert.Equal(-50.50m, rows[1].Amount);
    }

    [Fact]
    public void ParseCsv_requires_header_and_mapping_when_unknown()
    {
        Assert.Throws<ValidationException>(() => ImportParser.ParseCsv("a\n", null));
        Assert.Throws<ValidationException>(() => ImportParser.ParseCsv("foo;bar;baz\n1;2;3", null));
    }

    [Fact]
    public void ParseCsv_uses_explicit_mapping()
    {
        var csv = """
            ColA,ColB,ColC
            2026-03-01,12.5,Cafe
            """;
        var rows = ImportParser.ParseCsv(
            csv,
            new ImportMapping(DateColumn: "ColA", AmountColumn: "ColB", DescriptionColumn: "ColC", Delimiter: ','));
        Assert.Single(rows);
        Assert.Equal(12.5m, rows[0].Amount);
        Assert.Equal("Cafe", rows[0].Description);
    }

    [Fact]
    public void ParseOfx_reads_statement_transactions()
    {
        var ofx = """
            OFXHEADER:100
            <OFX>
            <STMTTRN>
            <DTPOSTED>20260315
            <TRNAMT>-33.40
            <MEMO>Mercado
            </STMTTRN>
            </OFX>
            """;
        var rows = ImportParser.ParseOfx(ofx);
        Assert.Single(rows);
        Assert.Equal(-33.40m, rows[0].Amount);
        Assert.Equal("Mercado", rows[0].Description);
        Assert.Equal(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), rows[0].Date);
    }

    [Fact]
    public void ParseOfx_throws_when_empty()
        => Assert.Throws<ValidationException>(() => ImportParser.ParseOfx("<OFX></OFX>"));
}
