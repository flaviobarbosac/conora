using Conora.Domain.Enums;
using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class Account : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string Name { get; private set; } = default!;
    public AccountKind Kind { get; private set; }
    public decimal Balance { get; private set; }
    public bool IsArchived { get; private set; }

    private Account()
    {
    }

    public static Account Create(string name, AccountKind kind, decimal openingBalance)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("name", "Nome da conta é obrigatório.");

        return new Account { Name = name.Trim(), Kind = kind, Balance = openingBalance };
    }

    public void Update(string name, AccountKind kind)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("name", "Nome da conta é obrigatório.");

        Name = name.Trim();
        Kind = kind;
    }

    public void SetArchived(bool archived) => IsArchived = archived;

    /// <summary>Applies a signed delta produced by an entry (or its reversal).</summary>
    public void ApplyDelta(decimal delta) => Balance += delta;
}
