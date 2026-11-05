using Conora.Domain.Entities;
using Xunit;

namespace Conora.UnitTests;

public class HotmartCourseReleaseTests
{
    [Fact]
    public void Eighth_day_is_nine_in_the_morning_in_Brazil_counting_the_purchase_day()
    {
        var acquired = new DateTime(2026, 11, 4, 22, 0, 0, DateTimeKind.Utc);
        var release = HotmartCoursePurchase.EighthDayMorningUtc(acquired);

        var tzId = OperatingSystem.IsWindows() ? "E. South America Standard Time" : "America/Sao_Paulo";
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(release, tz);
        Assert.Equal(new DateTime(2026, 11, 11, 9, 0, 0), local);
    }

    [Fact]
    public void Only_complete_grants_and_cancel_or_refund_request_revokes()
    {
        Assert.True(HotmartCoursePurchase.CompletesPurchase("PURCHASE_COMPLETE"));
        Assert.False(HotmartCoursePurchase.CompletesPurchase("PURCHASE_APPROVED"));
        Assert.True(HotmartCoursePurchase.RevokesAccess("PURCHASE_CANCELED"));
        Assert.True(HotmartCoursePurchase.RevokesAccess("PURCHASE_PROTEST"));
        Assert.False(HotmartCoursePurchase.RevokesAccess("PURCHASE_COMPLETE"));
    }

    [Fact]
    public void Revoke_keeps_the_grant_and_opens_two_days_to_export()
    {
        var acquired = new DateTime(2026, 11, 1, 15, 0, 0, DateTimeKind.Utc);
        var purchase = HotmartCoursePurchase.Schedule("aluno@example.com", "HP1", 1, acquired);
        purchase.GrantAccess();
        purchase.Revoke();

        Assert.True(purchase.Approved);
        Assert.NotNull(purchase.RevokedAt);
        Assert.Equal(purchase.RevokedAt.Value.AddDays(2), purchase.ExportUntilUtc);
    }

    [Fact]
    public void Cancel_before_the_grant_does_not_open_an_export_window()
    {
        var acquired = new DateTime(2026, 11, 1, 15, 0, 0, DateTimeKind.Utc);
        var purchase = HotmartCoursePurchase.Schedule("aluno@example.com", "HP2", 1, acquired);
        purchase.Revoke();

        Assert.False(purchase.Approved);
        Assert.Null(purchase.ExportUntilUtc);
    }
}
