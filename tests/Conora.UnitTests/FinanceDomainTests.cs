using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class FinanceDomainTests
{
    [Fact]
    public void System_chart_has_seven_roots_and_tithe_under_discount()
    {
        var roots = SystemChartAccounts.All.Where(c => c.Level == ChartAccountLevel.Root).ToList();
        Assert.Equal(7, roots.Count);
        Assert.Contains(SystemChartAccounts.All, c => c.Code == SystemChartAccounts.Tithe);
        Assert.Equal(ChartSection.Discount, SystemChartAccounts.All.First(c => c.Code == SystemChartAccounts.Tithe).Section);
    }

    [Fact]
    public void System_chart_account_cannot_be_renamed()
    {
        var account = ChartAccount.CreateSystem(SystemChartAccounts.All.First(c => c.Level == ChartAccountLevel.Analytical), Guid.NewGuid());

        Assert.Throws<SystemChartAccountProtectedException>(() => account.EnsureNotSystem());
        Assert.Throws<SystemChartAccountProtectedException>(() => account.Rename("Outro"));
    }

    [Fact]
    public void User_analytical_can_be_renamed()
    {
        var account = ChartAccount.CreateAnalytical("Extra", Guid.NewGuid(), ChartSection.Essential);
        account.Rename("Extra 2");
        Assert.Equal("Extra 2", account.Name);
    }

    [Fact]
    public void Transfer_nets_to_zero_across_accounts()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var entry = Entry.Create(EntryType.Transfer, 100m, DateTime.UtcNow, "Transfer", accountId: from, contraAccountId: to);

        Assert.Equal(0m, entry.BalanceEffects().Sum(e => e.Delta));
        Assert.False(entry.AffectsMonthlyResult);
        Assert.Null(entry.ChartAccountId);
    }

    [Fact]
    public void Card_payment_is_not_an_expense()
    {
        var entry = Entry.Create(EntryType.CardPayment, 500m, DateTime.UtcNow, "Invoice", accountId: Guid.NewGuid(), creditCardId: Guid.NewGuid());

        Assert.False(entry.AffectsMonthlyResult);
    }

    [Fact]
    public void Card_purchase_competence_is_purchase_month_and_closing_day_moves_invoice()
    {
        var card = CreditCard.Create("Card", 5000m, closingDay: 10, dueDay: 20, paymentAccountId: null);
        var purchase = CardPurchase.Create(card, 300m, new DateTime(2026, 10, 20), 3, Guid.NewGuid(), "TV");

        Assert.Equal("2026-10", purchase.CompetenceYm);
        Assert.Equal("2026-11", purchase.FirstInvoiceYm);

        var plan = purchase.InstallmentPlan();
        Assert.Equal(3, plan.Count);
        Assert.Equal(300m, plan.Sum(p => p.Amount));
        Assert.Equal("2027-01", plan[2].InvoiceYm);
    }

    [Fact]
    public void Reopen_requires_reason_and_owner()
    {
        var owner = Guid.NewGuid();
        var lockRow = MonthLock.Close("2026-09", owner);

        Assert.Throws<ValidationException>(() => lockRow.Reopen("", owner));
        Assert.Throws<ForbiddenException>(() => lockRow.Reopen("Ajuste de lançamento", Guid.NewGuid()));

        lockRow.Reopen("Ajuste de lançamento", owner);
        Assert.False(lockRow.IsClosed);
        Assert.Equal("Ajuste de lançamento", lockRow.ReopenReason);
    }

    [Fact]
    public void Expired_subscription_is_read_only()
    {
        var now = DateTime.UtcNow;
        var sub = WorkspaceSubscription.Activate(PlanKind.Monthly1490, now.AddMonths(-2));

        Assert.False(sub.IsWritable(now));
        Assert.Equal(SubscriptionStatus.Expired, sub.EffectiveStatus(now));
        Assert.Equal(14.90m, WorkspaceSubscription.PriceOf(PlanKind.Monthly1490));
    }

    [Fact]
    public void Competence_add_months_crosses_year()
    {
        Assert.Equal("2027-01", Competence.AddMonths("2026-11", 2));
        Assert.True(Competence.IsValid("2026-10"));
        Assert.False(Competence.IsValid("2026-13"));
    }

    [Fact]
    public void Competence_range_inclusive_counts_start_and_due()
    {
        var months = Competence.RangeInclusive("2026-11", "2027-01");
        Assert.Equal(["2026-11", "2026-12", "2027-01"], months);
    }

    [Fact]
    public void Display_numbers_follow_siblings_and_prefix_children()
    {
        var income = ChartAccount.CreateSystem(SystemChartAccounts.All.First(c => c.Code == "INC"), null);
        var salary = ChartAccount.CreateAnalytical("Salário", income.Id, ChartSection.Income, 1);
        var extra = ChartAccount.CreateAnalytical("Extra", income.Id, ChartSection.Income, 2);
        var discount = ChartAccount.CreateSystem(SystemChartAccounts.All.First(c => c.Code == "DISC"), null);

        ChartAccountDisplayNumbers.Apply([income, salary, extra, discount]);

        Assert.Equal("1", income.DisplayNumber);
        Assert.Equal("1.1", salary.DisplayNumber);
        Assert.Equal("1.2", extra.DisplayNumber);
        Assert.Equal("2", discount.DisplayNumber);
    }

    [Fact]
    public void Display_numbers_renumber_after_sibling_removed()
    {
        var income = ChartAccount.CreateSystem(SystemChartAccounts.All.First(c => c.Code == "INC"), null);
        var first = ChartAccount.CreateAnalytical("A", income.Id, ChartSection.Income, 1);
        var second = ChartAccount.CreateAnalytical("B", income.Id, ChartSection.Income, 2);
        var third = ChartAccount.CreateAnalytical("C", income.Id, ChartSection.Income, 3);
        ChartAccountDisplayNumbers.Apply([income, first, second, third]);

        ChartAccountDisplayNumbers.Apply([income, second, third]);

        Assert.Equal("1.1", second.DisplayNumber);
        Assert.Equal("1.2", third.DisplayNumber);
    }

    [Fact]
    public void Life_project_requires_start_and_due_window()
    {
        var due = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var project = LifeProject.Create("Reserva", 1200m, due, "2026-11", Guid.NewGuid());
        Assert.Equal("2026-11", project.ContributionStartYm);
        Assert.Equal(5, Competence.RangeInclusive(project.ContributionStartYm, Competence.From(project.DueDate)).Count);
        Assert.Throws<ValidationException>(() => project.Update("Reserva", 1200m, due, "2027-04", project.ChartAccountId));
    }

    [Fact]
    public void Life_project_allows_multiple_projects_on_same_chart_account()
    {
        var due = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var accountId = Guid.NewGuid();
        var first = LifeProject.Create("Viagem", 5000m, due, "2026-10", accountId);
        var second = LifeProject.Create("Reserva", 1200m, due, "2026-10", accountId);
        Assert.Equal(accountId, first.ChartAccountId);
        Assert.Equal(accountId, second.ChartAccountId);
        Assert.NotEqual(first.Name, second.Name);
    }

    [Fact]
    public void Life_project_contribution_never_goes_below_zero()
    {
        var due = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var project = LifeProject.Create("Reserva", 1200m, due, "2026-10", Guid.NewGuid());
        project.ApplyContribution(100m);
        project.ApplyContribution(-250m);
        Assert.Equal(0m, project.AccumulatedAmount);
        project.ApplyContribution(80m);
        Assert.Equal(80m, project.AccumulatedAmount);
    }
}
