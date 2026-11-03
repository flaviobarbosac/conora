using Conora.Domain.Enums;
using Conora.Services;
using Xunit;

namespace Conora.UnitTests;

public class PatrimonyBudgetReflectionTests
{
    [Fact]
    public void Income_and_expense_count_on_the_budget_and_move_the_item()
    {
        Assert.True(BudgetService.PatrimonyLaunchCountsInBudget(EntryType.Income));
        Assert.True(BudgetService.PatrimonyLaunchCountsInBudget(EntryType.Expense));
        Assert.True(BudgetService.CountsInBudgetActual(EntryType.Income));
        Assert.True(BudgetService.CountsInBudgetActual(EntryType.Expense));
    }

    [Fact]
    public void Life_project_aporte_and_transfers_do_not_move_patrimony()
    {
        Assert.False(BudgetService.PatrimonyLaunchCountsInBudget(EntryType.ProjectContribution));
        Assert.False(BudgetService.PatrimonyLaunchCountsInBudget(EntryType.Contribution));
        Assert.False(BudgetService.PatrimonyLaunchCountsInBudget(EntryType.Transfer));
        Assert.False(BudgetService.PatrimonyLaunchCountsInBudget(EntryType.CardPayment));
    }
}
