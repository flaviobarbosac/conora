using System.Globalization;
using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class MonthCloseReminderTests
{
    [Theory]
    [InlineData("2026-10-01T12:00:00Z", true)]
    [InlineData("2026-10-05T12:00:00Z", true)]
    [InlineData("2026-10-06T12:00:00Z", false)]
    [InlineData("2026-10-15T12:00:00Z", false)]
    public void Active_only_on_brazil_days_1_to_5(string utc, bool expected)
    {
        var instant = DateTime.Parse(utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Equal(expected, MonthCloseReminder.IsActive(instant));
    }
}
