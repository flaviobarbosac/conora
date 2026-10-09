using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Conora.Domain.Exceptions;
using Conora.Services.Contracts;

namespace Conora.Services.Common;

internal sealed record ParsedImportRow(string RawJson, DateTime Date, decimal Amount, string Description);

/// <summary>Parses bank statement files (CSV with auto-detected or mapped columns, and OFX) into neutral rows.</summary>
internal static partial class ImportParser
{
    private static readonly string[] DateFormats =
        ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd", "dd-MM-yyyy", "yyyyMMdd", "dd.MM.yyyy"];

    public static List<ParsedImportRow> ParseCsv(string content, ImportMapping? mapping)
    {
        var lines = SplitLines(content);
        if (lines.Count < 2)
            throw new ValidationException("content", "O CSV precisa de cabeçalho e ao menos uma linha.");

        var delimiter = mapping?.Delimiter ?? DetectDelimiter(lines[0]);
        var header = SplitCsvLine(lines[0], delimiter);

        var dateIdx = ResolveColumn(header, mapping?.DateColumn, "data", "date");
        var amountIdx = ResolveColumn(header, mapping?.AmountColumn, "valor", "amount", "value");
        var descIdx = ResolveColumn(header, mapping?.DescriptionColumn, "descri", "histor", "memo", "lancamento", "lançamento", "description");
        if (dateIdx < 0 || amountIdx < 0 || descIdx < 0)
            throw new ValidationException("mapping", "Não foi possível identificar as colunas de data, valor e descrição. Informe o mapeamento.");

        var rows = new List<ParsedImportRow>();
        for (var i = 1; i < lines.Count; i++)
        {
            var cells = SplitCsvLine(lines[i], delimiter);
            if (cells.Length <= Math.Max(dateIdx, Math.Max(amountIdx, descIdx)))
                throw new ValidationException("content", $"Linha {i + 1} do CSV tem colunas faltando.");

            var date = ParseDate(cells[dateIdx]) ?? throw new ValidationException("content", $"Data inválida na linha {i + 1}.");
            var amount = ParseAmount(cells[amountIdx]) ?? throw new ValidationException("content", $"Valor inválido na linha {i + 1}.");
            var description = cells[descIdx].Trim();
            rows.Add(new ParsedImportRow(RawJson(header, cells), date, amount, string.IsNullOrWhiteSpace(description) ? "Importado" : description));
        }

        return rows;
    }

    public static List<ParsedImportRow> ParseOfx(string content)
    {
        var rows = new List<ParsedImportRow>();
        foreach (Match block in StatementRegex().Matches(content))
        {
            var body = block.Groups[1].Value;
            var posted = Tag(body, "DTPOSTED");
            var date = posted is { Length: >= 8 } ? ParseDate(posted[..8]) : null;
            var amount = ParseAmount(Tag(body, "TRNAMT"));
            if (date is null || amount is null)
                continue;

            var description = Tag(body, "MEMO") ?? Tag(body, "NAME") ?? "Importado OFX";
            rows.Add(new ParsedImportRow(
                System.Text.Json.JsonSerializer.Serialize(new { raw = body.Trim() }),
                date.Value,
                amount.Value,
                description.Trim()));
        }

        if (rows.Count == 0)
            throw new ValidationException("content", "Nenhuma transação encontrada no OFX.");

        return rows;
    }

    private static string? Tag(string body, string tag)
    {
        var match = Regex.Match(body, $@"<{tag}>\s*([^<\r\n]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    internal static DateTime? ParseDate(string? text)
        => DateTime.TryParseExact(text?.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d.Date, DateTimeKind.Utc)
            : null;

    internal static decimal? ParseAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var cleaned = text.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").Replace("\u00a0", "").Trim();
        var negative = cleaned.StartsWith('-') || cleaned.StartsWith('(') || cleaned.EndsWith('-');
        cleaned = cleaned.Trim('-', '(', ')', '+');

        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');
        if (lastComma >= 0 && lastDot >= 0)
        {
            // The right-most separator is the decimal one.
            cleaned = lastComma > lastDot
                ? cleaned.Replace(".", "").Replace(',', '.')
                : cleaned.Replace(",", "");
        }
        else if (lastComma >= 0)
        {
            cleaned = cleaned.Replace(',', '.');
        }
        else if (lastDot >= 0 && cleaned.Count(c => c == '.') > 1)
        {
            cleaned = cleaned.Replace(".", "");
        }

        if (!decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            return null;

        return decimal.Round(negative ? -value : value, 2);
    }

    private static int ResolveColumn(string[] header, string? explicitColumn, params string[] hints)
    {
        if (!string.IsNullOrWhiteSpace(explicitColumn))
        {
            if (int.TryParse(explicitColumn, out var index))
                return index >= 0 && index < header.Length ? index : -1;

            return Array.FindIndex(header, h => h.Trim().Equals(explicitColumn.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return Array.FindIndex(header, h => hints.Any(hint => h.Contains(hint, StringComparison.OrdinalIgnoreCase)));
    }

    private static char DetectDelimiter(string headerLine)
    {
        var candidates = new[] { ';', ',', '\t' };
        return candidates.OrderByDescending(c => headerLine.Count(x => x == c)).First();
    }

    private static List<string> SplitLines(string content)
        => content.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

    private static string[] SplitCsvLine(string line, char delimiter)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == delimiter && !quoted)
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        cells.Add(current.ToString());
        return cells.ToArray();
    }

    private static string RawJson(string[] header, string[] cells)
    {
        var map = new Dictionary<string, string>();
        for (var i = 0; i < cells.Length; i++)
            map[i < header.Length && !string.IsNullOrWhiteSpace(header[i]) ? header[i].Trim() : $"col{i}"] = cells[i];

        return System.Text.Json.JsonSerializer.Serialize(map);
    }

    [GeneratedRegex(@"<STMTTRN>(.*?)</STMTTRN>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StatementRegex();
}
