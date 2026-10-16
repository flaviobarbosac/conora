using Conora.Domain;
using Conora.Domain.Catalog;
using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class User : ModelBase
{
    public string Name { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string? Cpf { get; private set; }
    public string? PasswordHash { get; private set; }
    public string? PasswordResetTokenHash { get; private set; }
    public DateTime? PasswordResetExpiresAtUtc { get; private set; }
    public string? GoogleId { get; private set; }
    public string? AppleId { get; private set; }
    public bool EmailVerified { get; private set; }

    public bool OnboardingCompleted { get; private set; }
    public string ThemeId { get; private set; } = ThemeCatalog.Clareza;
    public bool QuietMode { get; private set; }
    /// <summary>When false, Conora system e-mails are not sent (user opt-out).</summary>
    public bool EmailsEnabled { get; private set; } = true;
    public int ReserveTargetMonths { get; private set; } = 6;
    public int MonthCloseDay { get; private set; } = 1;
    public bool IncludeAbsoluteValuesInRetrospective { get; private set; }

    private User()
    {
    }

    public static User Create(string name, string email, string? cpf = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Nome é obrigatório.", nameof(name));
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email é obrigatório.", nameof(email));

        string? normalizedCpf = null;
        if (!string.IsNullOrWhiteSpace(cpf))
        {
            if (!BrazilianCpf.TryNormalize(cpf, out var parsed))
                throw new ArgumentException("CPF inválido.", nameof(cpf));
            normalizedCpf = parsed;
        }

        return new User
        {
            Name = name.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            Cpf = normalizedCpf
        };
    }

    public void SetPasswordHash(string passwordHash)
    {
        PasswordHash = passwordHash;
    }

    public void IssuePasswordReset(string tokenHash, DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Token inválido.", nameof(tokenHash));
        if (expiresAtUtc <= DateTime.UtcNow)
            throw new ArgumentException("Prazo inválido.", nameof(expiresAtUtc));

        PasswordResetTokenHash = tokenHash;
        PasswordResetExpiresAtUtc = expiresAtUtc;
    }

    public bool PasswordResetIsActive(string tokenHash)
        => !string.IsNullOrEmpty(PasswordResetTokenHash)
           && string.Equals(PasswordResetTokenHash, tokenHash, StringComparison.Ordinal)
           && PasswordResetExpiresAtUtc > DateTime.UtcNow;

    /// <summary>
    /// Sets a local password and clears the reset token. Google and Apple ids stay,
    /// so the same account can sign in with e-mail and password afterwards.
    /// </summary>
    public void ApplyPassword(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Senha inválida.", nameof(passwordHash));

        PasswordHash = passwordHash;
        PasswordResetTokenHash = null;
        PasswordResetExpiresAtUtc = null;
        EmailVerified = true;
    }

    public void SetGoogleId(string googleId)
    {
        GoogleId = googleId;
        EmailVerified = true;
    }

    public void SetAppleId(string appleId)
    {
        AppleId = appleId;
        EmailVerified = true;
    }

    public void MarkEmailVerified() => EmailVerified = true;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Nome é obrigatório.", nameof(name));
        Name = name.Trim();
    }

    public void CompleteOnboarding() => OnboardingCompleted = true;

    public void UpdatePreferences(
        string themeId,
        bool quietMode,
        bool emailsEnabled,
        int reserveTargetMonths,
        int monthCloseDay,
        bool includeAbsoluteValuesInRetrospective)
    {
        var errors = new Dictionary<string, string[]>();
        var theme = ThemeCatalog.Normalize(themeId);
        if (!ThemeCatalog.IsKnown(theme))
            errors["themeId"] = ["Tema inválido."];
        if (reserveTargetMonths is < 1 or > 24)
            errors["reserveTargetMonths"] = ["Meta da reserva deve ter entre 1 e 24 meses."];
        if (monthCloseDay is < 1 or > 31)
            errors["monthCloseDay"] = ["Dia de fechamento deve ser entre 1 e 31."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        ThemeId = theme;
        QuietMode = quietMode;
        EmailsEnabled = emailsEnabled;
        ReserveTargetMonths = reserveTargetMonths;
        MonthCloseDay = monthCloseDay;
        IncludeAbsoluteValuesInRetrospective = includeAbsoluteValuesInRetrospective;
    }

    public void RequestDeletion()
    {
        DeletedAt = DateTime.UtcNow;
        Version += 1;
    }
}
