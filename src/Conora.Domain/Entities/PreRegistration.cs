namespace Conora.Domain.Entities;

/// <summary>
/// Pending signup held until the e-mail confirmation link is opened.
/// Only then a <see cref="User"/> row is created.
/// </summary>
public class PreRegistration
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Name { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string? Cpf { get; private set; }
    public string? PasswordHash { get; private set; }
    public string? GoogleId { get; private set; }
    public string? AppleId { get; private set; }
    public bool IsTrainingStudent { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private PreRegistration()
    {
    }

    public static PreRegistration CreatePassword(
        string name,
        string email,
        string cpf,
        string passwordHash,
        string tokenHash,
        DateTime expiresAtUtc,
        bool isTrainingStudent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(cpf);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (expiresAtUtc <= DateTime.UtcNow)
            throw new ArgumentException("Prazo inválido.", nameof(expiresAtUtc));

        return new PreRegistration
        {
            Name = name.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            Cpf = cpf,
            PasswordHash = passwordHash,
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAtUtc,
            IsTrainingStudent = isTrainingStudent,
        };
    }

    public static PreRegistration CreateGoogle(
        string name,
        string email,
        string googleId,
        string tokenHash,
        DateTime expiresAtUtc,
        bool isTrainingStudent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(googleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (expiresAtUtc <= DateTime.UtcNow)
            throw new ArgumentException("Prazo inválido.", nameof(expiresAtUtc));

        return new PreRegistration
        {
            Name = name.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            GoogleId = googleId,
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAtUtc,
            IsTrainingStudent = isTrainingStudent,
        };
    }

    public static PreRegistration CreateApple(
        string name,
        string email,
        string appleId,
        string tokenHash,
        DateTime expiresAtUtc,
        bool isTrainingStudent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(appleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (expiresAtUtc <= DateTime.UtcNow)
            throw new ArgumentException("Prazo inválido.", nameof(expiresAtUtc));

        return new PreRegistration
        {
            Name = name.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            AppleId = appleId,
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAtUtc,
            IsTrainingStudent = isTrainingStudent,
        };
    }

    public bool IsActive(string tokenHash)
        => string.Equals(TokenHash, tokenHash, StringComparison.Ordinal)
           && ExpiresAtUtc > DateTime.UtcNow;

    public void RefreshToken(string tokenHash, DateTime expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (expiresAtUtc <= DateTime.UtcNow)
            throw new ArgumentException("Prazo inválido.", nameof(expiresAtUtc));
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
    }
}
