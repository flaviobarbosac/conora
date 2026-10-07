using Conora.Domain;

namespace Conora.Domain.Entities;

public class User : ModelBase
{
    public string Name { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string? Cpf { get; private set; }
    public string? PasswordHash { get; private set; }
    public string? GoogleId { get; private set; }
    public bool EmailVerified { get; private set; }

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

    public void SetGoogleId(string googleId)
    {
        GoogleId = googleId;
        EmailVerified = true;
    }

    public void MarkEmailVerified() => EmailVerified = true;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Nome é obrigatório.", nameof(name));
        Name = name.Trim();
    }

    public void RequestDeletion()
    {
        DeletedAt = DateTime.UtcNow;
        Version += 1;
    }
}
