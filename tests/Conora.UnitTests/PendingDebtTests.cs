using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Xunit;

namespace Conora.UnitTests;

public class PendingDebtTests
{
    [Fact]
    public void Partial_payment_reduces_open_amount_and_keeps_the_debt_open()
    {
        var debt = PendingDebt.Open("2026-08", Guid.NewGuid(), 650m);

        var applied = debt.ApplyPayment(200m);

        Assert.Equal(200m, applied);
        Assert.Equal(450m, debt.OpenAmount);
        Assert.Null(debt.PaidAt);
        Assert.True(debt.IsOpen);
    }

    [Fact]
    public void Full_payment_sets_the_payment_date_and_clears_the_balance()
    {
        var debt = PendingDebt.Open("2026-08", Guid.NewGuid(), 650m);

        var applied = debt.ApplyPayment(650m);

        Assert.Equal(650m, applied);
        Assert.Equal(0m, debt.OpenAmount);
        Assert.NotNull(debt.PaidAt);
        Assert.False(debt.IsOpen);
    }

    [Fact]
    public void Payment_above_the_balance_quits_the_debt_and_returns_only_what_was_used()
    {
        var debt = PendingDebt.Open("2026-08", Guid.NewGuid(), 650m);

        var applied = debt.ApplyPayment(800m);

        Assert.Equal(650m, applied);
        Assert.Equal(0m, debt.OpenAmount);
        Assert.NotNull(debt.PaidAt);
    }

    [Fact]
    public void Paid_debt_rejects_another_payment()
    {
        var debt = PendingDebt.Open("2026-08", Guid.NewGuid(), 100m);
        debt.ApplyPayment(100m);

        Assert.Throws<ValidationException>(() => debt.ApplyPayment(10m));
    }

    [Fact]
    public void Sequential_allocation_uses_remaining_across_selected_debts()
    {
        var categoryId = Guid.NewGuid();
        var first = PendingDebt.Open("2026-08", categoryId, 1750m);
        var second = PendingDebt.Open("2026-09", categoryId, 1750m);
        var remaining = 1000m;

        remaining -= first.ApplyPayment(remaining);
        if (remaining > 0)
            remaining -= second.ApplyPayment(remaining);

        Assert.Equal(0m, remaining);
        Assert.Equal(750m, first.OpenAmount);
        Assert.Null(first.PaidAt);
        Assert.Equal(1750m, second.OpenAmount);
        Assert.Null(second.PaidAt);
    }

    [Fact]
    public void Sequential_allocation_pays_first_debt_fully_then_partial_on_next()
    {
        var categoryId = Guid.NewGuid();
        var first = PendingDebt.Open("2026-08", categoryId, 1750m);
        var second = PendingDebt.Open("2026-09", categoryId, 1750m);
        var remaining = 2000m;

        remaining -= first.ApplyPayment(remaining);
        if (remaining > 0)
            remaining -= second.ApplyPayment(remaining);

        Assert.Equal(0m, remaining);
        Assert.Equal(0m, first.OpenAmount);
        Assert.NotNull(first.PaidAt);
        Assert.Equal(1500m, second.OpenAmount);
        Assert.Null(second.PaidAt);
    }
}
