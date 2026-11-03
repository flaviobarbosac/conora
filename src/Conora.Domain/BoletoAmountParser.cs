using System.Text.RegularExpressions;

namespace Conora.Domain;

/// <summary>
/// Reads the document amount from a FEBRABAN linha digitável or barcode.
/// A zero value is an open amount and is ignored.
/// </summary>
public static partial class BoletoAmountParser
{
    [GeneratedRegex(@"(\d{5})\D?(\d{5})\D+(\d{5})\D?(\d{6})\D+(\d{5})\D?(\d{6})\D+(\d)\D+(\d{14})")]
    private static partial Regex BankLine();

    [GeneratedRegex(@"(?<!\d)(\d{44})(?!\d)")]
    private static partial Regex Barcode44();

    [GeneratedRegex(@"(?<!\d)(\d{48})(?!\d)")]
    private static partial Regex Utility48();

    public static decimal? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var line = BankLine().Match(text);
        if (line.Success)
        {
            var amount = FromCents(line.Groups[8].Value[^10..]);
            if (amount is > 0)
                return amount;
        }

        var barcode = Barcode44().Match(text);
        if (barcode.Success)
        {
            var amount = FromCents(barcode.Groups[1].Value.Substring(9, 10));
            if (amount is > 0)
                return amount;
        }

        var utility = Utility48().Match(text);
        if (utility.Success)
        {
            var digits = utility.Groups[1].Value;
            if (digits[2] is '6' or '7')
            {
                var amount = FromCents(digits.Substring(4, 11));
                if (amount is > 0)
                    return amount;
            }
        }

        return null;
    }

    private static decimal? FromCents(string digits)
    {
        if (!ulong.TryParse(digits, out var cents))
            return null;
        return decimal.Round(cents / 100m, 2);
    }
}
