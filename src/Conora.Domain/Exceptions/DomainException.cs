namespace Conora.Domain.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message)
    {
    }
}

public sealed class ValidationException : DomainException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("Um ou mais campos são inválidos.")
    {
        Errors = errors;
    }

    public ValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = new[] { error } })
    {
    }
}

public sealed class UserNotFoundException : DomainException
{
    public UserNotFoundException(Guid id) : base($"Usuário '{id}' não encontrado.")
    {
    }
}

public sealed class DuplicateEmailException : DomainException
{
    public DuplicateEmailException(string email)
        : base(
            string.IsNullOrWhiteSpace(email)
                ? "Este e-mail já tem uma conta Conora. Entre com esse acesso."
                : $"Este e-mail ({email.Trim()}) já tem uma conta Conora. Entre com esse acesso.")
    {
    }
}

public sealed class DuplicateCpfException : DomainException
{
    public DuplicateCpfException() : base("Já existe um usuário cadastrado com este CPF.")
    {
    }
}

public sealed class UniqueConstraintViolationException : DomainException
{
    public string ConstraintName { get; }

    public UniqueConstraintViolationException(string constraintName)
        : base($"Violação de restrição de unicidade: '{constraintName}'.")
    {
        ConstraintName = constraintName;
    }
}

public sealed class InvalidCredentialsException : DomainException
{
    public InvalidCredentialsException() : base("Credenciais inválidas.")
    {
    }
}

/// <summary>Login attempted with an identity that has no Conora account yet.</summary>
public sealed class AccountNotFoundException : DomainException
{
    public AccountNotFoundException()
        : base("Não encontramos uma conta com este acesso. Crie uma conta para continuar.")
    {
    }
}

/// <summary>Register attempted with Google/Apple that already has a Conora account.</summary>
public sealed class SocialAccountExistsException : DomainException
{
    public SocialAccountExistsException(string provider)
        : base($"Já existe uma conta Conora com este {provider}. Use a tela Entrar.")
    {
    }
}

public sealed class InvalidTokenException : DomainException
{
    public InvalidTokenException() : base("Token inválido ou expirado.")
    {
    }
}

public sealed class GoogleAuthDisabledException : DomainException
{
    public GoogleAuthDisabledException() : base("Login Google não configurado.")
    {
    }
}

public sealed class AppleAuthDisabledException : DomainException
{
    public AppleAuthDisabledException() : base("Login Apple não configurado.")
    {
    }
}
