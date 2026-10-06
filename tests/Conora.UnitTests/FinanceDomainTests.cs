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
    public void System_seed_has_contributions_and_no_income_discount_category()
    {
        Assert.Contains(SystemCategories.All, c => c.Code == SystemCategories.Contributions);
        Assert.DoesNotContain(SystemCategories.All, c => c.Name.Contains("Descontos sobre renda", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void System_category_cannot_be_renamed_or_deleted()
    {
        var category = Category.CreateSystem(SystemCategories.All[0]);

        Assert.Throws<SystemCategoryProtectedException>(() => category.EnsureNotSystem());
        Assert.Throws<SystemCategoryProtectedException>(() => category.Update("Outro", false));
    }

    [Fact]
    public void Income_discount_category_cannot_be_created()
    {
        Assert.Throws<ValidationException>(() => Category.Create("Descontos sobre renda", CategoryKind.Expense));
    }

    [Fact]
    public void Transfer_nets_to_zero_across_accounts()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var entry = Entry.Create(EntryType.Transfer, 100m, DateTime.UtcNow, "Transfer", accountId: from, contraAccountId: to);

        Assert.Equal(0m, entry.BalanceEffects().Sum(e => e.Delta));
        Assert.False(entry.AffectsMonthlyResult);
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
}
