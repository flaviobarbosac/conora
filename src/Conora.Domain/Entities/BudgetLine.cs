using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class BudgetLine : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public Guid BudgetId { get; private set; }
    public Guid ChartAccountId { get; private set; }
    public decimal PlannedAmount { get; private set; }

    private BudgetLine()
    {
    }

    public static BudgetLine Create(Guid budgetId, Guid chartAccountId, decimal plannedAmount)
    {
        var line = new BudgetLine { BudgetId = budgetId, ChartAccountId = chartAccountId };
        line.SetPlanned(plannedAmount);
        return line;
    }

    public void SetPlanned(decimal plannedAmount)
    {
        if (plannedAmount < 0)
            throw new ValidationException("plannedAmount", "Valor planejado não pode ser negativo.");

        PlannedAmount = decimal.Round(plannedAmount, 2);
    }
}
