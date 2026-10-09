using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class PatrimonyItem : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public Guid ChartAccountId { get; private set; }
    public string Name { get; private set; } = default!;
    public decimal Amount { get; private set; }

    private PatrimonyItem()
    {
    }

    public static PatrimonyItem Create(Guid chartAccountId, string name, decimal amount)
    {
        var item = new PatrimonyItem { ChartAccountId = chartAccountId };
        item.SetName(name);
        item.SetAmount(amount);
        return item;
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("name", "Nome do item é obrigatório.");

        Name = name.Trim();
    }

    /// <summary>Only the current value is kept; there is no revaluation history (spec v1.1).</summary>
    public void SetAmount(decimal amount)
    {
        if (amount < 0)
            throw new ValidationException("amount", "Valor não pode ser negativo.");

        Amount = decimal.Round(amount, 2);
    }
}
