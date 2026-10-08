using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class PatrimonyItem : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public Guid ChartAccountId { get; private set; }
    public decimal Amount { get; private set; }

    private PatrimonyItem()
    {
    }

    public static PatrimonyItem Create(Guid chartAccountId, decimal amount)
    {
        var item = new PatrimonyItem { ChartAccountId = chartAccountId };
        item.SetAmount(amount);
        return item;
    }

    /// <summary>Only the current value is kept; there is no revaluation history (spec v1.1).</summary>
    public void SetAmount(decimal amount)
    {
        if (amount < 0)
            throw new ValidationException("amount", "Valor não pode ser negativo.");

        Amount = decimal.Round(amount, 2);
    }
}
