using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class PaymentPunctualityTests
{
    private static readonly DateTime Due = PaymentPunctuality.DueDate(2026, 12, 10);

    [Fact]
    public void Paid_before_due_day_is_on_time()
        => Assert.True(PaymentPunctuality.IsOnTime(new DateTime(2026, 12, 3, 15, 0, 0, DateTimeKind.Utc), Due));

    [Fact]
    public void Paid_on_due_day_is_on_time_at_any_hour()
        => Assert.True(PaymentPunctuality.IsOnTime(new DateTime(2026, 12, 10, 23, 59, 0, DateTimeKind.Utc), Due));

    [Fact]
    public void Paid_after_due_day_is_late()
        => Assert.False(PaymentPunctuality.IsOnTime(new DateTime(2026, 12, 11, 0, 1, 0, DateTimeKind.Utc), Due));

    [Theory]
    [InlineData(2026, 2, 31, 28)]
    [InlineData(2028, 2, 30, 29)]
    [InlineData(2026, 11, 31, 30)]
    [InlineData(2026, 12, 5, 5)]
    public void Due_day_past_month_end_falls_on_last_day(int year, int month, int expenseDay, int expected)
        => Assert.Equal(expected, PaymentPunctuality.DueDate(year, month, expenseDay).Day);
}
