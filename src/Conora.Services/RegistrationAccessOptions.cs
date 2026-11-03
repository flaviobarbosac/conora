namespace Conora.Services;

/// <summary>
/// When closed, only listed e-mails can create an account or sign in.
/// Everyone else gets <see cref="ClosedMessage"/>.
/// </summary>
public sealed class RegistrationAccessOptions
{
    public const string SectionName = "RegistrationAccess";
    public const string ClosedMessage = "Acesso em breve.";

    public bool Closed { get; set; }

    public string[] AllowedEmails { get; set; } = [];

    public bool Allows(string? email)
    {
        if (!Closed)
            return true;
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var normalized = email.Trim();
        foreach (var allowed in AllowedEmails)
        {
            if (string.Equals(allowed?.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
