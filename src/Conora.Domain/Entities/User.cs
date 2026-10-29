using Conora.Domain;
using Conora.Domain.Catalog;
using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class User : ModelBase
{
    public string Name { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string? Phone { get; private set; }
    public string? Cep { get; private set; }
    public string? Cpf { get; private set; }
    public string? PasswordHash { get; private set; }
    public string? PasswordResetTokenHash { get; private set; }
    public DateTime? PasswordResetExpiresAtUtc { get; private set; }
    public string? EmailVerificationTokenHash { get; private set; }
    public DateTime? EmailVerificationExpiresAtUtc { get; private set; }
    public string? GoogleId { get; private set; }
    public string? AppleId { get; private set; }
    public bool EmailVerified { get; private set; }

    public bool OnboardingCompleted { get; private set; }

    /// <summary>
    /// Training student (Leomar Campos course purchase): 90-day free trial (30 + 60).
    /// Everyone else gets 30 days. Not a store SKU — AccessPolicy resolves length.
    /// </summary>
    public bool IsTrainingStudent { get; private set; }
    public string ThemeId { get; private set; } = ThemeCatalog.Clareza;
    public bool QuietMode { get; private set; }
    /// <summary>When false, Conora system e-mails are not sent (user opt-out).</summary>
    public bool EmailsEnabled { get; private set; } = true;
    public int ReserveTargetMonths { get; private set; } = 6;
    public int MonthCloseDay { get; private set; } = 1;
    public bool IncludeAbsoluteValuesInRetrospective { get; private set; }
    /// <summary>When true, Ian shows the spoken launch and waits for approval before saving.</summary>
    public bool VoiceEntryConfirm { get; private set; } = true;
    /// <summary>Percent at which a budget account enters Attention (yellow). Limit at 100% and Exceeded above stay fixed.</summary>
    public int BudgetAlertPercent { get; private set; } = 70;
    /// <summary>When true, Ian and Clara portraits and voice bands follow milestone age. When false, both stay at age 30 / adult.</summary>
    public bool PhotoEvolutionEnabled { get; private set; } = true;

    /// <summary>Object-store key for the profile photo (not the bytes).</summary>
    public string? PhotoObjectKey { get; private set; }
    /// <summary>SHA-256 hex of the stored photo bytes.</summary>
    public string? PhotoContentHash { get; private set; }
    /// <summary>MIME type of the stored photo (image/jpeg, image/png, image/webp).</summary>
    public string? PhotoContentType { get; private set; }

    /// <summary>Set when the user acknowledges the paid-plan contract and LGPD. Later subscriptions skip that screen.</summary>
    public DateTime? ContractAcknowledgedAt { get; private set; }

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

    /// <summary>Clears any outstanding password-reset token (e.g. security incident).</summary>
    public void ClearPasswordReset()
    {
        PasswordResetTokenHash = null;
        PasswordResetExpiresAtUtc = null;
    }

    /// <summary>
    /// Sets a local password and clears the reset token. Google and Apple ids stay,
    /// so the same account can sign in with e-mail and password afterwards.
    /// </summary>
    public void ApplyPassword(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Senha inválida.", nameof(passwordHash));

        PasswordHash = passwordHash;
        ClearPasswordReset();
    }

    public void SetGoogleId(string googleId) => GoogleId = googleId;

    public void SetAppleId(string appleId) => AppleId = appleId;

    public void IssueEmailVerification(string tokenHash, DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Token inválido.", nameof(tokenHash));
        if (expiresAtUtc <= DateTime.UtcNow)
            throw new ArgumentException("Prazo inválido.", nameof(expiresAtUtc));

        EmailVerificationTokenHash = tokenHash;
        EmailVerificationExpiresAtUtc = expiresAtUtc;
    }

    public bool EmailVerificationIsActive(string tokenHash)
        => !string.IsNullOrEmpty(EmailVerificationTokenHash)
           && string.Equals(EmailVerificationTokenHash, tokenHash, StringComparison.Ordinal)
           && EmailVerificationExpiresAtUtc > DateTime.UtcNow;

    /// <summary>
    /// Marks the address verified. Keeps the hash until a new IssueEmailVerification
    /// so a second open of the same e-mail link (app cold start + appUrlOpen) stays idempotent.
    /// </summary>
    public void MarkEmailVerified()
    {
        EmailVerified = true;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Nome é obrigatório.", nameof(name));
        Name = name.Trim();
    }

    public void UpdateContact(string? phone, string? cep)
    {
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        Cep = string.IsNullOrWhiteSpace(cep) ? null : NormalizeCep(cep);
    }

    public void CompleteOnboarding() => OnboardingCompleted = true;

    public void SetTrainingStudent(bool isTrainingStudent) => IsTrainingStudent = isTrainingStudent;

    public void AcknowledgeContract() => ContractAcknowledgedAt ??= DateTime.UtcNow;

    private static string NormalizeCep(string cep)
    {
        var digits = new string(cep.Where(char.IsDigit).ToArray());
        if (digits.Length != 8)
            throw new ArgumentException("CEP deve ter 8 dígitos.", nameof(cep));
        return $"{digits[..5]}-{digits[5..]}";
    }

    public void UpdatePreferences(
        string themeId,
        bool quietMode,
        bool emailsEnabled,
        int reserveTargetMonths,
        int monthCloseDay,
        bool includeAbsoluteValuesInRetrospective,
        bool voiceEntryConfirm = true,
        int budgetAlertPercent = 70,
        bool photoEvolutionEnabled = true)
    {
        var errors = new Dictionary<string, string[]>();
        var theme = ThemeCatalog.Normalize(themeId);
        if (!ThemeCatalog.IsKnown(theme))
            errors["themeId"] = ["Tema inválido."];
        if (reserveTargetMonths is < 1 or > 24)
            errors["reserveTargetMonths"] = ["Meta da reserva deve ter entre 1 e 24 meses."];
        if (monthCloseDay is < 1 or > 31)
            errors["monthCloseDay"] = ["Dia de fechamento deve ser entre 1 e 31."];
        if (budgetAlertPercent is < 1 or > 99)
            errors["budgetAlertPercent"] = ["Alerta do orçamento deve ser entre 1% e 99%."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        ThemeId = theme;
        QuietMode = quietMode;
        EmailsEnabled = emailsEnabled;
        ReserveTargetMonths = reserveTargetMonths;
        MonthCloseDay = monthCloseDay;
        IncludeAbsoluteValuesInRetrospective = includeAbsoluteValuesInRetrospective;
        VoiceEntryConfirm = voiceEntryConfirm;
        BudgetAlertPercent = budgetAlertPercent;
        PhotoEvolutionEnabled = photoEvolutionEnabled;
    }

    public void RequestDeletion()
    {
        DeletedAt = DateTime.UtcNow;
        Version += 1;
    }

    public void SetPhoto(string objectKey, string contentHash, string contentType)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
            throw new ArgumentException("Chave da foto inválida.", nameof(objectKey));
        if (string.IsNullOrWhiteSpace(contentHash) || contentHash.Length != 64)
            throw new ArgumentException("Hash da foto inválido.", nameof(contentHash));
        if (string.IsNullOrWhiteSpace(contentType) || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Tipo da foto inválido.", nameof(contentType));

        PhotoObjectKey = objectKey.Trim();
        PhotoContentHash = contentHash.Trim().ToLowerInvariant();
        PhotoContentType = contentType.Trim().ToLowerInvariant();
    }

    public void ClearPhoto()
    {
        PhotoObjectKey = null;
        PhotoContentHash = null;
        PhotoContentType = null;
    }
}
