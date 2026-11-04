using System.Globalization;
using ClosedXML.Excel;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed record ReportEntryRow(
    DateTime Date,
    string Competence,
    string Type,
    decimal Amount,
    string? Account,
    string? DestinationAccount,
    string? Category,
    string? Member,
    string? Description);

/// <summary>Monthly report workbook: logo, Conora colors, summary and formatted entries.</summary>
public static class ReportWorkbookBuilder
{
    private static readonly XLColor Navy = XLColor.FromHtml("#172638");
    private static readonly XLColor Primary = XLColor.FromHtml("#375984");
    private static readonly XLColor White = XLColor.FromHtml("#ffffff");
    private static readonly XLColor Ink = XLColor.FromHtml("#172638");

    public static byte[] Build(
        string competenceYm,
        MonthlyReportResponse report,
        IReadOnlyList<ReportEntryRow> entries,
        byte[]? logoPng)
    {
        using var workbook = new XLWorkbook();
        var title = Title(competenceYm);
        WriteSummary(workbook, title, report, logoPng);
        WriteEntries(workbook, title, entries, logoPng);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static void WriteSummary(
        XLWorkbook workbook,
        string title,
        MonthlyReportResponse report,
        byte[]? logoPng)
    {
        var ws = workbook.Worksheets.Add("Resumo");
        PlaceHeader(ws, title, logoPng);

        var s = report.Summary;
        var row = 4;
        row = WritePairHeader(ws, row, "Item", "Valor");
        row = WriteMoney(ws, row, "Renda", s.IncomeTotal);
        row = WriteMoney(ws, row, "Despesas", s.ExpenseTotal);
        row = WriteMoney(ws, row, "Resultado", s.Result);
        row = WriteMoney(ws, row, "Contribuições/Doações", s.ContributionsTotal);
        row = WriteMoney(ws, row, "Compras no cartão", s.CardPurchasesTotal);
        row = WriteMoney(ws, row, "Aportes em projetos", s.ProjectContributionsTotal);
        row++;
        row = WritePairHeader(ws, row, "Categoria", "Valor");
        foreach (var item in report.ByAccount)
            row = WriteMoney(ws, row, item.CategoryName, item.Amount);

        ws.Columns(1, 2).AdjustToContents(3, Math.Max(row, 4));
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 28);
    }

    private static void WriteEntries(
        XLWorkbook workbook,
        string title,
        IReadOnlyList<ReportEntryRow> entries,
        byte[]? logoPng)
    {
        var ws = workbook.Worksheets.Add("Lançamentos");
        PlaceHeader(ws, title, logoPng);

        var headers = new[]
        {
            "Data", "Competência", "Tipo", "Valor", "Conta", "Conta destino", "Categoria", "Membro", "Descrição"
        };
        const int headerRow = 4;
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = headers[c];
            StyleHeader(cell);
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var r = headerRow + 1 + i;
            var date = ws.Cell(r, 1);
            date.Value = entry.Date.Date;
            date.Style.DateFormat.Format = "dd/mm/yyyy";
            date.Style.Font.FontColor = Ink;
            Text(ws.Cell(r, 2), entry.Competence);
            Text(ws.Cell(r, 3), entry.Type);
            var amount = ws.Cell(r, 4);
            amount.Value = entry.Amount;
            amount.Style.NumberFormat.Format = "R$ #,##0.00";
            amount.Style.Font.FontColor = Ink;
            Text(ws.Cell(r, 5), entry.Account);
            Text(ws.Cell(r, 6), entry.DestinationAccount);
            Text(ws.Cell(r, 7), entry.Category);
            Text(ws.Cell(r, 8), entry.Member);
            Text(ws.Cell(r, 9), entry.Description);
        }

        ws.Columns(1, headers.Length).AdjustToContents(headerRow, headerRow + Math.Max(entries.Count, 1));
        ws.SheetView.FreezeRows(headerRow);
    }

    private static void PlaceHeader(IXLWorksheet ws, string title, byte[]? logoPng)
    {
        ws.Cell(1, 1).Value = "Conora";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontColor = Navy;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        if (logoPng is { Length: > 0 })
        {
            using var stream = new MemoryStream(logoPng);
            ws.AddPicture(stream).MoveTo(ws.Cell(1, 1)).WithSize(140, 40);
        }

        ws.Row(1).Height = 48;
        ws.Cell(2, 1).Value = title;
        ws.Cell(2, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Style.Font.FontColor = Navy;
        ws.Cell(2, 1).Style.Font.FontSize = 14;
    }

    private static int WritePairHeader(IXLWorksheet ws, int row, string left, string right)
    {
        var a = ws.Cell(row, 1);
        var b = ws.Cell(row, 2);
        a.Value = left;
        b.Value = right;
        StyleHeader(a);
        StyleHeader(b);
        return row + 1;
    }

    private static int WriteMoney(IXLWorksheet ws, int row, string label, decimal value)
    {
        Text(ws.Cell(row, 1), label);
        var amount = ws.Cell(row, 2);
        amount.Value = value;
        amount.Style.NumberFormat.Format = "R$ #,##0.00";
        amount.Style.Font.FontColor = Ink;
        return row + 1;
    }

    private static void Text(IXLCell cell, string? value)
    {
        cell.Value = value ?? "";
        cell.Style.Font.FontColor = Ink;
    }

    private static void StyleHeader(IXLCell cell)
    {
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontColor = White;
        cell.Style.Fill.BackgroundColor = Primary;
    }

    private static string Title(string competenceYm)
    {
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        var parts = competenceYm.Split('-');
        var date = new DateTime(int.Parse(parts[0], culture), int.Parse(parts[1], culture), 1);
        var month = culture.TextInfo.ToTitleCase(date.ToString("MMMM", culture));
        return $"Relatório de {month} de {date.Year}";
    }
}
