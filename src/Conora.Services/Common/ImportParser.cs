using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Conora.Domain.Exceptions;
using Conora.Services.Contracts;
using UglyToad.PdfPig;

namespace Conora.Services.Common;

internal sealed record ParsedImportAccount(
    string? BankCode,
    string? Agency,
    string? AccountNumber,
    string? CheckDigit);

internal sealed record ParsedImportRow(string RawJson, DateTime Date, decimal Amount, string Description);

internal sealed record ParsedImportFile(
    IReadOnlyList<ParsedImportRow> Rows,
    ParsedImportAccount? Account,
    DateTime? PeriodStart = null,
    DateTime? PeriodEnd = null);

/// <summary>Parses bank statement files (CSV, OFX, PDF) into neutral rows.</summary>
internal static partial class ImportParser
{
    private const string SupportHint =
        "Entre em contato com o Suporte Técnico — o caminho mais rápido é pelo Feedback no menu do Conora.";

    private static readonly string[] DateFormats =
        ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "yyyy-MM-dd", "dd-MM-yyyy", "yyyyMMdd", "dd.MM.yyyy"];

    public static ParsedImportFile Parse(
        ImportFormatHint format,
        string content,
        ImportMapping? mapping,
        byte[]? pdfBytes = null,
        string? fileName = null)
        => format switch
        {
            ImportFormatHint.Ofx => ParseOfx(content, fileName),
            ImportFormatHint.Pdf => ParsePdf(pdfBytes ?? Encoding.UTF8.GetBytes(content), fileName),
            _ => Finalize(ParseCsv(content, mapping), account: null, content, fileName),
        };

    private static ValidationException SupportRequired(string missing)
        => new(
            "content",
            $"Não foi possível identificar {missing} neste extrato. Esses dados são obrigatórios para registrar os lançamentos. {SupportHint}");

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

    public static ParsedImportFile ParseOfx(string content, string? fileName = null)
    {
        var account = ExtractOfxAccount(content);
        var rows = new List<ParsedImportRow>();
        foreach (Match block in StatementRegex().Matches(content))
        {
            var body = block.Groups[1].Value;
            var posted = Tag(body, "DTPOSTED");
            var date = posted is { Length: >= 8 } ? ParseDate(posted[..8]) : null;
            var amount = ParseAmount(Tag(body, "TRNAMT"));
            if (date is null || amount is null || amount == 0)
                continue;

            var description = Tag(body, "MEMO") ?? Tag(body, "NAME") ?? "Importado OFX";
            rows.Add(new ParsedImportRow(
                System.Text.Json.JsonSerializer.Serialize(new { raw = body.Trim() }),
                date.Value,
                amount.Value,
                description.Trim()));
        }

        return Finalize(rows, account, content, fileName);
    }

    public static ParsedImportFile ParsePdf(byte[] pdfBytes, string? fileName = null)
    {
        if (pdfBytes.Length == 0)
            throw SupportRequired("o conteúdo do arquivo PDF");

        string text;
        try
        {
            using var document = PdfDocument.Open(pdfBytes);
            var builder = new StringBuilder();
            foreach (var page in document.GetPages())
                builder.AppendLine(page.Text);
            text = builder.ToString();
        }
        catch (Exception ex)
        {
            throw new ValidationException(
                "content",
                $"Não foi possível ler o PDF: {ex.Message}. {SupportHint}");
        }

        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException(
                "content",
                $"O PDF não tem texto legível (pode ser imagem escaneada). Exporte OFX ou CSV se o banco permitir. {SupportHint}");

        return ParsePdfText(text, fileName);
    }

    /// <summary>Parses already-extracted PDF text (also used by unit tests).</summary>
    internal static ParsedImportFile ParsePdfText(string text, string? fileName = null)
    {
        var account = ExtractTextAccount(text);
        var rows = ParseStatementLines(text);
        return Finalize(rows, account, text, fileName);
    }

    private static ParsedImportFile Finalize(
        IReadOnlyList<ParsedImportRow> rows,
        ParsedImportAccount? account,
        string? text,
        string? fileName)
    {
        var period = ExtractPeriod(text, fileName, rows);
        if (period is null)
            throw SupportRequired("o período do extrato (data inicial e final)");

        if (rows.Count == 0)
            throw SupportRequired("os lançamentos com data, crédito/débito e valor");

        foreach (var row in rows)
        {
            if (row.Date == default)
                throw SupportRequired("a data de um ou mais lançamentos");
            if (row.Amount == 0)
                throw SupportRequired("se cada lançamento é crédito ou débito e o valor");
            if (string.IsNullOrWhiteSpace(row.Description))
                throw SupportRequired("a descrição de um ou mais lançamentos");
        }

        return new ParsedImportFile(rows, account, period.Value.Start, period.Value.End);
    }

    /// <summary>Captures statement period from header text, OFX tags, file name or row dates.</summary>
    internal static (DateTime Start, DateTime End)? ExtractPeriod(
        string? text,
        string? fileName,
        IReadOnlyList<ParsedImportRow> rows)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            var fromHeader = ExtractPeriodFromText(text);
            if (fromHeader is not null)
                return fromHeader;

            var ofxStart = Tag(text, "DTSTART");
            var ofxEnd = Tag(text, "DTEND");
            if (ofxStart is { Length: >= 8 } && ofxEnd is { Length: >= 8 })
            {
                var start = ParseDate(ofxStart[..8]);
                var end = ParseDate(ofxEnd[..8]);
                if (start is not null && end is not null)
                    return OrderPeriod(start.Value, end.Value);
            }
        }

        var fromName = ExtractPeriodFromFileName(fileName);
        if (fromName is not null)
            return fromName;

        if (rows.Count > 0)
        {
            var start = rows.Min(r => r.Date);
            var end = rows.Max(r => r.Date);
            return OrderPeriod(start, end);
        }

        return null;
    }

    private static (DateTime Start, DateTime End)? ExtractPeriodFromText(string text)
    {
        foreach (Match match in PeriodRangeRegex().Matches(text))
        {
            var start = ParseDate(match.Groups["start"].Value);
            var end = ParseDate(match.Groups["end"].Value);
            if (start is not null && end is not null)
                return OrderPeriod(start.Value, end.Value);
        }

        var monthYear = MonthYearRegex().Match(text);
        if (monthYear.Success)
        {
            var year = int.Parse(monthYear.Groups["year"].Value, CultureInfo.InvariantCulture);
            var month = monthYear.Groups["month"].Success
                ? PortugueseMonthNumber(monthYear.Groups["month"].Value)
                : int.Parse(monthYear.Groups["mm"].Value, CultureInfo.InvariantCulture);
            if (month is >= 1 and <= 12)
            {
                var start = DateTime.SpecifyKind(new DateTime(year, month, 1), DateTimeKind.Utc);
                var end = DateTime.SpecifyKind(start.AddMonths(1).AddDays(-1), DateTimeKind.Utc);
                return (start, end);
            }
        }

        var dayHeaders = PortugueseDayHeaderRegex().Matches(text);
        if (dayHeaders.Count > 0)
        {
            DateTime? min = null;
            DateTime? max = null;
            foreach (Match dayMatch in dayHeaders)
            {
                var date = ParsePortugueseLongDate(
                    dayMatch.Groups["day"].Value,
                    dayMatch.Groups["month"].Value,
                    dayMatch.Groups["year"].Value);
                if (date is null)
                    continue;
                min = min is null || date < min ? date : min;
                max = max is null || date > max ? date : max;
            }

            if (min is not null && max is not null)
                return OrderPeriod(min.Value, max.Value);
        }

        return null;
    }

    private static (DateTime Start, DateTime End)? ExtractPeriodFromFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var match = FileNamePeriodRegex().Match(fileName);
        if (!match.Success)
            return null;

        var start = ParseDate($"{match.Groups["d1"].Value}/{match.Groups["m1"].Value}/{match.Groups["y1"].Value}");
        var end = ParseDate($"{match.Groups["d2"].Value}/{match.Groups["m2"].Value}/{match.Groups["y2"].Value}");
        if (start is null || end is null)
            return null;
        return OrderPeriod(start.Value, end.Value);
    }

    private static (DateTime Start, DateTime End) OrderPeriod(DateTime a, DateTime b)
        => a <= b ? (a, b) : (b, a);

    private static int PortugueseMonthNumber(string monthText)
        => monthText.Trim().ToLowerInvariant() switch
        {
            "janeiro" => 1,
            "fevereiro" => 2,
            "março" or "marco" => 3,
            "abril" => 4,
            "maio" => 5,
            "junho" => 6,
            "julho" => 7,
            "agosto" => 8,
            "setembro" => 9,
            "outubro" => 10,
            "novembro" => 11,
            "dezembro" => 12,
            _ => 0,
        };

    private static ParsedImportAccount? ExtractOfxAccount(string content)
    {
        var bank = Tag(content, "BANKID");
        var agency = Tag(content, "BRANCHID");
        var account = Tag(content, "ACCTID");
        if (string.IsNullOrWhiteSpace(bank) && string.IsNullOrWhiteSpace(account))
            return null;

        string? number = account;
        string? digit = null;
        if (!string.IsNullOrWhiteSpace(account) && account.Contains('-'))
        {
            var parts = account.Split('-', 2);
            number = parts[0];
            digit = parts[1];
        }

        return new ParsedImportAccount(
            DigitsOnly(bank, 3),
            DigitsOnly(agency, 20),
            DigitsOnly(number, 20),
            digit?.Trim().ToUpperInvariant());
    }

    private static ParsedImportAccount? ExtractTextAccount(string text)
    {
        var bank = MatchGroup(text, @"banco\s*(?:c[oó]d(?:igo)?\.?)?\s*[:.]?\s*(\d{3})", 1)
                   ?? MatchGroup(text, @"\b(\d{3})\s*-\s*[A-Za-zÀ-ÿ]", 1)
                   ?? ResolveBankName(text);
        var agency = MatchGroup(text, @"ag[eê]ncia\s*[:.]?\s*(\d{1,6})", 1);
        var accountMatch = Regex.Match(text, @"conta\s*(?:corrente)?\s*[:.]?\s*(\d{3,12})(?:[-\/\s]?(\d|[A-Z]))?", RegexOptions.IgnoreCase);
        if (bank is null && agency is null && !accountMatch.Success)
            return null;

        return new ParsedImportAccount(
            DigitsOnly(bank, 3),
            DigitsOnly(agency, 20),
            DigitsOnly(accountMatch.Success ? accountMatch.Groups[1].Value : null, 20),
            accountMatch.Success && accountMatch.Groups[2].Success
                ? accountMatch.Groups[2].Value.Trim().ToUpperInvariant()
                : null);
    }

    private static string? ResolveBankName(string text)
    {
        if (Regex.IsMatch(text, @"banco\s+inter\b", RegexOptions.IgnoreCase))
            return "077";
        if (Regex.IsMatch(text, @"nubank|nu\s+pagamentos", RegexOptions.IgnoreCase))
            return "260";
        if (Regex.IsMatch(text, @"banco\s+do\s+brasil\b", RegexOptions.IgnoreCase))
            return "001";
        if (Regex.IsMatch(text, @"\bitau\b", RegexOptions.IgnoreCase))
            return "341";
        if (Regex.IsMatch(text, @"bradesco\b", RegexOptions.IgnoreCase))
            return "237";
        if (Regex.IsMatch(text, @"caixa\s+econ[oô]mica", RegexOptions.IgnoreCase))
            return "104";
        return null;
    }

    private static List<ParsedImportRow> ParseStatementLines(string text)
    {
        var interRows = ParseInterStyleLines(text);
        if (interRows.Count > 0)
            return interRows;

        var rows = new List<ParsedImportRow>();
        var linePattern = StatementLineRegex();
        foreach (Match match in linePattern.Matches(text))
        {
            var date = ParseDate(match.Groups["date"].Value);
            var amount = ParseAmount(match.Groups["amount"].Value);
            var description = match.Groups["desc"].Value.Trim();
            if (date is null || amount is null || string.IsNullOrWhiteSpace(description))
                continue;
            if (IsPeriodOrBalanceNoise(description))
                continue;

            rows.Add(new ParsedImportRow(
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    date = match.Groups["date"].Value,
                    description,
                    amount = match.Groups["amount"].Value,
                }),
                date.Value,
                amount.Value,
                Truncate(description, 250)));
        }

        return rows;
    }

    /// <summary>
    /// Banco Inter (and similar) PDFs: "10 de Setembro de 2026" then
    /// Pix enviado/recebido / Pagamento de Titulo with quoted memo and amount.
    /// </summary>
    private static List<ParsedImportRow> ParseInterStyleLines(string text)
    {
        var dayMatches = PortugueseDayHeaderRegex().Matches(text);
        if (dayMatches.Count == 0)
            return [];

        var rows = new List<ParsedImportRow>();
        for (var i = 0; i < dayMatches.Count; i++)
        {
            var dayMatch = dayMatches[i];
            var date = ParsePortugueseLongDate(
                dayMatch.Groups["day"].Value,
                dayMatch.Groups["month"].Value,
                dayMatch.Groups["year"].Value);
            if (date is null)
                continue;

            var start = dayMatch.Index + dayMatch.Length;
            var end = i + 1 < dayMatches.Count ? dayMatches[i + 1].Index : text.Length;
            var section = text[start..end];

            foreach (Match txn in InterTransactionRegex().Matches(section))
            {
                var amount = ParseAmount(txn.Groups["amount"].Value);
                if (amount is null || amount == 0)
                    continue;

                var kind = txn.Groups["kind"].Value.Trim();
                amount = ApplyMovementSign(kind, amount.Value);
                if (amount == 0)
                    continue;

                var memo = txn.Groups["memo"].Success ? txn.Groups["memo"].Value.Trim() : "";
                var description = string.IsNullOrWhiteSpace(memo) ? kind : $"{kind}: {memo}";
                rows.Add(new ParsedImportRow(
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        date = date.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                        description,
                        amount = amount.Value,
                        kind,
                    }),
                    date.Value,
                    amount.Value,
                    Truncate(description, 250)));
            }
        }

        return rows;
    }

    /// <summary>Forces debit/credit from movement kind when the bank labels it clearly.</summary>
    private static decimal ApplyMovementSign(string kind, decimal amount)
    {
        var abs = Math.Abs(amount);
        var key = kind.Trim().ToLowerInvariant();
        if (key.Contains("recebid", StringComparison.Ordinal)
            || key.Contains("crédit", StringComparison.Ordinal)
            || key.Contains("credit", StringComparison.Ordinal)
            || key.Contains("depósito", StringComparison.Ordinal)
            || key.Contains("deposito", StringComparison.Ordinal)
            || key.Contains("rendiment", StringComparison.Ordinal))
            return abs;

        if (key.Contains("enviad", StringComparison.Ordinal)
            || key.Contains("pagamento", StringComparison.Ordinal)
            || key.Contains("débito", StringComparison.Ordinal)
            || key.Contains("debito", StringComparison.Ordinal)
            || key.Contains("tarifa", StringComparison.Ordinal)
            || key.Contains("compra", StringComparison.Ordinal)
            || key.Contains("saque", StringComparison.Ordinal)
            || key is "ted" or "doc")
            return -abs;

        return amount;
    }

    private static DateTime? ParsePortugueseLongDate(string dayText, string monthText, string yearText)
    {
        if (!int.TryParse(dayText, out var day) || !int.TryParse(yearText, out var year))
            return null;

        var month = PortugueseMonthNumber(monthText);
        if (month == 0)
            return null;

        try
        {
            return DateTime.SpecifyKind(new DateTime(year, month, day), DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool IsPeriodOrBalanceNoise(string description)
    {
        if (description.Length < 4)
            return true;

        if (description.Contains("Saldo total", StringComparison.OrdinalIgnoreCase)
            || description.Contains("Saldo do dia", StringComparison.OrdinalIgnoreCase)
            || description.Contains("Saldo disponível", StringComparison.OrdinalIgnoreCase)
            || description.Contains("Saldo bloqueado", StringComparison.OrdinalIgnoreCase)
            || description.Contains("Período", StringComparison.OrdinalIgnoreCase))
            return true;

        // Header "01/09/2026 a 30/09/2026Saldo total..." → desc "a" / "a 30/09/2026..."
        return Regex.IsMatch(description, @"^a(\s+\d{2}/\d{2}/\d{4})?", RegexOptions.IgnoreCase);
    }

    private static string? Tag(string body, string tag)
    {
        var match = Regex.Match(body, $@"<{tag}>\s*([^<\r\n]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? MatchGroup(string text, string pattern, int group)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[group].Value.Trim() : null;
    }

    private static string? DigitsOnly(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return null;
        return digits.Length <= maxLength ? digits : digits[^maxLength..];
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max];

    internal static DateTime? ParseDate(string? text)
        => DateTime.TryParseExact(text?.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d.Date, DateTimeKind.Utc)
            : null;

    internal static decimal? ParseAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var cleaned = text.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").Replace("\u00a0", "").Trim();
        var negative = cleaned.StartsWith('-') || cleaned.StartsWith('(') || cleaned.EndsWith('-') || cleaned.EndsWith("D", StringComparison.OrdinalIgnoreCase);
        if (cleaned.EndsWith("C", StringComparison.OrdinalIgnoreCase))
            negative = false;
        cleaned = cleaned.Trim('-', '(', ')', '+');
        if (cleaned.EndsWith("D", StringComparison.OrdinalIgnoreCase) || cleaned.EndsWith("C", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[..^1];

        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');
        if (lastComma >= 0 && lastDot >= 0)
        {
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

    /// <summary>Common bank PDF line: date, description, signed amount (requires R$ or cents).</summary>
    [GeneratedRegex(
        @"(?<date>\d{2}/\d{2}/\d{4}|\d{4}-\d{2}-\d{2})\s+(?<desc>.+?)\s+(?<amount>-?\(?R\$\s*[\d.,]+\)?|-?\(?[\d.]+,[\d]{2}\)?|-?\(?[\d]+[.][\d]{2}\)?\s*[CD]?)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex StatementLineRegex();

    [GeneratedRegex(
        @"(?<day>\d{1,2})\s+de\s+(?<month>janeiro|fevereiro|mar[cç]o|abril|maio|junho|julho|agosto|setembro|outubro|novembro|dezembro)\s+de\s+(?<year>\d{4})",
        RegexOptions.IgnoreCase)]
    private static partial Regex PortugueseDayHeaderRegex();

    [GeneratedRegex(
        @"(?<kind>Pix enviado|Pix recebido|Pagamento de T[ií]tulo|Pagamento efetuado|Transfer[eê]ncia(?:\s+enviada|\s+recebida)?|TED|DOC|D[eé]bito|Cr[eé]dito|Tarifa|Compra|Saque|Dep[oó]sito|Rendimento)[^:]*:\s*(?:""(?<memo>[^""]*)"")?\s*(?<amount>-?\s*R\$\s*[\d.,]+|\(?-?[\d.]+,[\d]{2}\)?\s*[CD]?)",
        RegexOptions.IgnoreCase)]
    private static partial Regex InterTransactionRegex();

    [GeneratedRegex(
        @"(?:per[ií]odo|extrato|movimenta[cç][aã]o|de)\s*:?\s*(?<start>\d{1,2}[/.\-]\d{1,2}[/.\-]\d{2,4}|\d{4}-\d{2}-\d{2})\s*(?:a|at[eé]|ate|-|–|—)\s*(?<end>\d{1,2}[/.\-]\d{1,2}[/.\-]\d{2,4}|\d{4}-\d{2}-\d{2})",
        RegexOptions.IgnoreCase)]
    private static partial Regex PeriodRangeRegex();

    [GeneratedRegex(
        @"(?:compet[eê]ncia|m[eê]s)\s*:?\s*(?:(?<month>janeiro|fevereiro|mar[cç]o|abril|maio|junho|julho|agosto|setembro|outubro|novembro|dezembro)\s*(?:de\s*)?(?<year>\d{4})|(?<mm>\d{1,2})[/.\-](?<year>\d{4})|(?<year>\d{4})-(?<mm>\d{2}))|(?<month>janeiro|fevereiro|mar[cç]o|abril|maio|junho|julho|agosto|setembro|outubro|novembro|dezembro)\s*/\s*(?<year>\d{4})",
        RegexOptions.IgnoreCase)]
    private static partial Regex MonthYearRegex();

    [GeneratedRegex(
        @"(?<d1>\d{2})[-_.](?<m1>\d{2})[-_.](?<y1>\d{4}).{0,12}(?<d2>\d{2})[-_.](?<m2>\d{2})[-_.](?<y2>\d{4})",
        RegexOptions.IgnoreCase)]
    private static partial Regex FileNamePeriodRegex();
}

internal enum ImportFormatHint
{
    Csv,
    Ofx,
    Pdf,
}
