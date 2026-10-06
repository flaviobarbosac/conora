namespace Conora.Domain;

public static class BrazilianCpf
{
    public static string DigitsOnly(string value)
        => new(value.Where(char.IsDigit).ToArray());

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var digits = DigitsOnly(value);
        if (digits.Length != 11)
            return false;

        if (digits.Distinct().Count() == 1)
            return false;

        if (!HasValidCheckDigits(digits))
            return false;

        normalized = digits;
        return true;
    }

    private static bool HasValidCheckDigits(string digits)
    {
        var numbers = digits.Select(c => c - '0').ToArray();
        var first = CheckDigit(numbers, 9, 10);
        var second = CheckDigit(numbers, 10, 11);
        return numbers[9] == first && numbers[10] == second;
    }

    private static int CheckDigit(int[] numbers, int length, int weightStart)
    {
        var sum = 0;
        for (var i = 0; i < length; i++)
            sum += numbers[i] * (weightStart - i);

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}

public static class LoginIdentifier
{
    public static bool IsEmail(string value)
        => value.Contains('@', StringComparison.Ordinal);
}
