namespace Conora.Domain.Catalog;

/// <summary>Operators who may open the aggregate console. No other account.</summary>
public static class ConsoleAccess
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "flaviobarbosa.vix@gmail.com",
        "lelcampos2009@gmail.com"
    };

    public static bool IsAllowed(string? email)
        => !string.IsNullOrWhiteSpace(email) && Allowed.Contains(email.Trim());
}
