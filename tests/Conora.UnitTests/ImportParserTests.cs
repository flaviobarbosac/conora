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
            <BANKACCTFROM>
            <BANKID>001
            <BRANCHID>1234
            <ACCTID>56789-0
            </BANKACCTFROM>
            <STMTTRN>
            <DTPOSTED>20260315
            <TRNAMT>-33.40
            <MEMO>Mercado
            </STMTTRN>
            </OFX>
            """;
        var parsed = ImportParser.ParseOfx(ofx);
        Assert.Single(parsed.Rows);
        Assert.Equal(-33.40m, parsed.Rows[0].Amount);
        Assert.Equal("Mercado", parsed.Rows[0].Description);
        Assert.Equal(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), parsed.Rows[0].Date);
        Assert.Equal("001", parsed.Account?.BankCode);
        Assert.Equal("1234", parsed.Account?.Agency);
        Assert.Equal("56789", parsed.Account?.AccountNumber);
        Assert.Equal("0", parsed.Account?.CheckDigit);
    }

    [Fact]
    public void ParseOfx_throws_when_empty()
        => Assert.Throws<ValidationException>(() => ImportParser.ParseOfx("<OFX></OFX>"));

    [Fact]
    public void ParsePdfText_reads_banco_inter_portuguese_day_headers()
    {
        var text = """
            Solicitado em: 08/10/2026 - 16h05
            CPF/CNPJ: 41.298.828/0001-55, Instituição: Banco Inter, Agência: 0001-9, Conta: 12631756-9
            Período: 01/09/2026 a 30/09/2026
            Saldo total R$ 1.596,06
            10 de Setembro de 2026 Saldo do dia: R$ 2.788,60
            Pix enviado: "Cp :00000000-Suaramaya Batista de Castro Barbosa"-R$ 13.000,00-R$ 12.211,40
            Pix recebido: "Cp :60746948-CAPERE INTELIGENCIA DA INFORMACAO LTDA -"R$ 15.000,00R$ 2.788,60
            17 de Setembro de 2026 Saldo do dia: R$ 2.578,56
            Pagamento de Titulo: "ES BRASIL SERVICOS E NEGOCIOS ADMINISTRATIVOS LTDA"-R$ 210,04R$ 2.578,56
            21 de Setembro de 2026 Saldo do dia: R$ 16.018,56
            Pix recebido: "Cp :31872495-SMART SYSTEM CONSULTING SERVICOS DE INFORMATICALTDA"R$ 13.440,00R$ 16.018,56
            22 de Setembro de 2026 Saldo do dia: R$ 15.596,06
            Pix enviado: "Cp :60701190-RECEITA FEDERAL"-R$ 422,50R$ 15.596,06
            """;

        var parsed = ImportParser.ParsePdfText(text);

        Assert.Equal(5, parsed.Rows.Count);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), parsed.PeriodStart);
        Assert.Equal(new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc), parsed.PeriodEnd);
        Assert.Equal(new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), parsed.Rows[0].Date);
        Assert.Equal(-13000m, parsed.Rows[0].Amount);
        Assert.Contains("Suaramaya", parsed.Rows[0].Description);
        Assert.Equal(15000m, parsed.Rows[1].Amount);
        Assert.Equal(-210.04m, parsed.Rows[2].Amount);
        Assert.Equal(13440m, parsed.Rows[3].Amount);
        Assert.Equal(-422.50m, parsed.Rows[4].Amount);
        Assert.Equal("077", parsed.Account?.BankCode);
        Assert.Equal("0001", parsed.Account?.Agency);
        Assert.Equal("12631756", parsed.Account?.AccountNumber);
        Assert.Equal("9", parsed.Account?.CheckDigit);
    }

    [Fact]
    public void ParsePdfText_missing_vital_data_asks_for_support_via_feedback()
    {
        var text = "Período: 01/09/2026 a 30/09/2026Saldo totalR$ 1.596,06Sem movimentação no formato antigo";
        var ex = Assert.Throws<ValidationException>(() => ImportParser.ParsePdfText(text));
        var detail = string.Join(' ', ex.Errors.SelectMany(e => e.Value));
        Assert.Contains("Não foi possível identificar", detail);
        Assert.Contains("Feedback", detail);
    }

    [Fact]
    public void ExtractPeriod_reads_file_name_range()
    {
        var period = ImportParser.ExtractPeriod(
            text: null,
            fileName: "Extrato-01-09-2026-a-30-09-2026-PDF.pdf",
            rows: []);
        Assert.NotNull(period);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), period!.Value.Start);
        Assert.Equal(new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc), period.Value.End);
    }

    [Fact]
    public void ParsePdf_reads_real_inter_september_file_when_present()
    {
        var path = @"D:\tmp\Extrato-01-09-2026-a-30-09-2026-PDF.pdf";
        if (!File.Exists(path))
            return;

        var parsed = ImportParser.ParsePdf(File.ReadAllBytes(path));
        Assert.Equal(5, parsed.Rows.Count);
        Assert.All(parsed.Rows, r => Assert.Equal(9, r.Date.Month));
        Assert.Equal(-13000m, parsed.Rows[0].Amount);
        Assert.Equal("077", parsed.Account?.BankCode);
    }
}
