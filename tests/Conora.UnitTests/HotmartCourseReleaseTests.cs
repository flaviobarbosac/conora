using Conora.Domain.Entities;
using Xunit;

namespace Conora.UnitTests;

public class HotmartCourseReleaseTests
{
    [Fact]
    public void Access_opens_98_days_after_the_purchase_and_the_login_message_uses_brazil_time()
    {
        var acquired = new DateTime(2026, 11, 4, 22, 0, 0, DateTimeKind.Utc);
        var release = HotmartCoursePurchase.AccessAvailableAtUtc(acquired);
        var purchase = HotmartCoursePurchase.Schedule("aluno@example.com", "HP0", 1, acquired);

        Assert.Equal(acquired.AddDays(98), release);
        Assert.Equal(release, purchase.UnlockAt);
        Assert.Equal(
            "Seu acesso fica disponível a partir de 10/02/2027 às 19:00.",
            HotmartCoursePurchase.AccessAvailableMessage(release));
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
