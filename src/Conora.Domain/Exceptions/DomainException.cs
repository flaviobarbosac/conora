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
    public DuplicateEmailException(string email) : base($"Já existe um usuário cadastrado com o email '{email}'.")
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
