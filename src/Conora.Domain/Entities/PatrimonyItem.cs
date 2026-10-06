using Conora.Domain.Enums;
using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class PatrimonyItem : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public PatrimonyKind Kind { get; private set; }
    public string Name { get; private set; } = default!;
    public decimal Amount { get; private set; }

    private PatrimonyItem()
    {
    }

    public static PatrimonyItem Create(PatrimonyKind kind, string name, decimal amount)
    {
        var item = new PatrimonyItem { Kind = kind };
        item.Update(name, amount);
        return item;
    }

    /// <summary>Only the current value is kept; there is no revaluation history (spec v1.1).</summary>
    public void Update(string name, decimal amount)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["Nome é obrigatório."];
        if (amount < 0) errors["amount"] = ["Valor não pode ser negativo."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        Name = name.Trim();
        Amount = decimal.Round(amount, 2);
    }
}
