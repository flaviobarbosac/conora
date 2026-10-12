using System.Text.RegularExpressions;

namespace Conora.Domain;

public static partial class BrazilianCnpj
{
    public static string DigitsOnly(string value)
        => new(value.Where(char.IsDigit).ToArray());

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var digits = DigitsOnly(value);
        if (digits.Length != 14)
            return false;

        if (digits.Distinct().Count() == 1)
            return false;

        if (!HasValidCheckDigits(digits))
            return false;

        normalized = digits;
        return true;
    }

    /// <summary>Extracts the first valid CNPJ found in free text (OCR lines).</summary>
    public static bool TryExtract(string? text, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (Match match in CnpjPattern().Matches(text))
        {
            if (TryNormalize(match.Value, out normalized))
                return true;
        }

        return false;
    }

    private static bool HasValidCheckDigits(string digits)
    {
        var numbers = digits.Select(c => c - '0').ToArray();
        var first = CheckDigit(numbers, 12, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        var second = CheckDigit(numbers, 13, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return numbers[12] == first && numbers[13] == second;
    }

    private static int CheckDigit(int[] numbers, int length, int[] weights)
    {
        var sum = 0;
        for (var i = 0; i < length; i++)
            sum += numbers[i] * weights[i];

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }

    [GeneratedRegex(@"\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2}|\d{14}")]
    private static partial Regex CnpjPattern();
}
