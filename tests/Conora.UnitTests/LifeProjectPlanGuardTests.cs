using Conora.Domain.Enums;
using Conora.Services;
using Xunit;

namespace Conora.UnitTests;

public class LifeProjectPlanGuardTests
{
    [Fact]
    public void Other_sections_stay_editable()
    {
        Assert.Null(BudgetService.ManualLifeProjectPlanRefusal(false, false));
        Assert.Null(BudgetService.ManualLifeProjectPlanRefusal(false, true));
    }

    [Fact]
    public void Linked_project_keeps_the_existing_refusal()
    {
        var message = BudgetService.ManualLifeProjectPlanRefusal(true, true);

        Assert.Equal(
            "O previsto desta conta vem do projeto de vida. Crie o projeto pela conta ou apague o projeto para liberar o valor.",
            message);
    }

    [Fact]
    public void Unlinked_life_project_account_refuses_manual_plan()
    {
        var message = BudgetService.ManualLifeProjectPlanRefusal(true, false);

        Assert.Equal(
            "Conta de projeto de vida não aceita previsto manual. Crie o projeto para gerar o rateio.",
            message);
    }
}

public class LifeProjectIncomeGuardTests
{
    [Fact]
    public void Income_outside_life_project_does_not_require_a_project()
    {
        Assert.Null(EntryService.IncomeLifeProjectRefusal(false, false));
    }

    [Fact]
    public void Income_on_life_project_without_id_is_refused()
    {
        Assert.Equal(
            EntryService.IncomeRequiresProjectMessage,
            EntryService.IncomeLifeProjectRefusal(true, false));
    }

    [Fact]
    public void Income_on_life_project_with_id_is_accepted()
    {
        Assert.Null(EntryService.IncomeLifeProjectRefusal(true, true));
    }
}

public class LifeProjectBudgetReflectionTests
{
    [Fact]
    public void Income_and_aporte_count_in_the_budget_actual()
    {
        Assert.True(BudgetService.CountsInBudgetActual(EntryType.Income));
        Assert.True(BudgetService.CountsInBudgetActual(EntryType.ProjectContribution));
        Assert.False(BudgetService.CountsInBudgetActual(EntryType.Transfer));
        Assert.False(BudgetService.CountsInBudgetActual(EntryType.CardPayment));
    }
}

public class LifeProjectRateioTests
{
    [Fact]
    public void Creation_schedule_writes_every_month_and_sums_to_the_goal()
    {
        var months = new[] { "2026-11", "2026-12", "2027-01" };

        var schedule = LifeProjectService.BuildParcelSchedule(100m, months);

        Assert.Equal(months, schedule.Keys.OrderBy(month => month).ToArray());
        Assert.Equal(100m, schedule.Values.Sum());
        Assert.Equal(33.33m, schedule["2026-11"]);
        Assert.Equal(33.34m, schedule["2027-01"]);
    }

    [Fact]
    public void Linked_entry_blocks_project_deletion()
    {
        Assert.Equal(
            LifeProjectService.DeleteBlockedMessage,
            LifeProjectService.DeleteBlockedByEntries(true));
        Assert.Null(LifeProjectService.DeleteBlockedByEntries(false));
    }
}
