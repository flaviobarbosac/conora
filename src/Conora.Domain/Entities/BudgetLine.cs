using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class BudgetLine : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public Guid BudgetId { get; private set; }
    public Guid CategoryId { get; private set; }
    public decimal PlannedAmount { get; private set; }

    /// <summary>Day of month (1-31) when this planned amount is expected. No month or year.</summary>
    public int? ExpenseDay { get; private set; }

    private BudgetLine()
    {
    }

    public static BudgetLine Create(Guid budgetId, Guid categoryId, decimal plannedAmount, int? expenseDay = null)
    {
        var line = new BudgetLine { BudgetId = budgetId, CategoryId = categoryId };
        line.SetPlanned(plannedAmount);
        line.SetExpenseDay(expenseDay);
        return line;
    }

    public void MoveCategory(Guid categoryId) => CategoryId = categoryId;

    public void SetPlanned(decimal plannedAmount)
    {
        if (plannedAmount < 0)
            throw new ValidationException("plannedAmount", "Valor planejado não pode ser negativo.");

        PlannedAmount = decimal.Round(plannedAmount, 2);
    }

    public void SetExpenseDay(int? expenseDay)
    {
        if (expenseDay is < 1 or > 31)
            throw new ValidationException("expenseDay", "Informe um dia entre 1 e 31.");

        ExpenseDay = expenseDay;
    }
}
