using System.Text;
using System.Text.Json;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Ports;
using Conora.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Conora.UnitTests;

public class StoreReceiptParsingTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid User = Guid.Parse("0199f0a2-0000-7000-8000-000000000001");

    [Theory]
    [InlineData("SUBSCRIPTION_STATE_ACTIVE", "2026-11-23T12:00:00Z", true)]
    [InlineData("SUBSCRIPTION_STATE_CANCELED", "2026-11-23T12:00:00Z", true)]
    [InlineData("SUBSCRIPTION_STATE_IN_GRACE_PERIOD", "2026-10-25T12:00:00Z", true)]
    [InlineData("SUBSCRIPTION_STATE_ACTIVE", "2026-10-22T12:00:00Z", false)]
    [InlineData("SUBSCRIPTION_STATE_PENDING", "2026-11-23T12:00:00Z", false)]
    [InlineData("SUBSCRIPTION_STATE_ON_HOLD", "2026-11-23T12:00:00Z", false)]
    [InlineData("SUBSCRIPTION_STATE_EXPIRED", "2026-10-22T12:00:00Z", false)]
    public void Google_entitlement_follows_state_and_expiry(string state, string expiry, bool entitled)
    {
        using var doc = JsonDocument.Parse($$"""
            {
              "subscriptionState": "{{state}}",
              "latestOrderId": "GPA.1234",
              "acknowledgementState": "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED",
              "externalAccountIdentifiers": { "obfuscatedExternalAccountId": "{{User}}" },
              "lineItems": [ { "productId": "conora.duo.yearly", "expiryTime": "{{expiry}}" } ]
            }
            """);

        var receipt = StoreReceiptValidator.ReadGoogleSubscription(doc.RootElement, "token-abc", null, Now);

        Assert.Equal(entitled, receipt.Entitled);
        Assert.Equal("conora.duo.yearly", receipt.ProductId);
        Assert.Equal("GPA.1234", receipt.TransactionId);
        Assert.Equal("token-abc", receipt.SubscriptionId);
        Assert.Equal(User, receipt.AccountId);
        Assert.Equal(DateTime.Parse(expiry).ToUniversalTime(), receipt.ExpiresAt);
        Assert.False(receipt.Sandbox);
    }

    [Fact]
    public void Google_license_tester_purchase_is_sandbox()
    {
        using var doc = JsonDocument.Parse("""
            {
              "subscriptionState": "SUBSCRIPTION_STATE_ACTIVE",
              "latestOrderId": "GPA.1234",
              "testPurchase": {},
              "lineItems": [ { "productId": "conora.duo.yearly", "expiryTime": "2026-11-23T12:00:00Z" } ]
            }
            """);

        Assert.True(StoreReceiptValidator.ReadGoogleSubscription(doc.RootElement, "token-abc", null, Now).Sandbox);
    }

    [Fact]
    public void Apple_transaction_is_entitled_until_expiry_and_keyed_by_original_id()
    {
        var receipt = StoreReceiptValidator.ReadAppleTransaction(
            AppleTransaction(expires: Now.AddDays(30)), "br.com.conora.app", "Sandbox", Now);

        Assert.True(receipt.Entitled);
        Assert.Equal("conora.individual.monthly", receipt.ProductId);
        Assert.Equal("2000000002", receipt.TransactionId);
        Assert.Equal("2000000001", receipt.SubscriptionId);
        Assert.Equal(User, receipt.AccountId);
        Assert.True(receipt.Sandbox);
    }

    [Fact]
    public void Apple_revoked_or_expired_transaction_is_not_entitled()
    {
        Assert.False(StoreReceiptValidator.ReadAppleTransaction(
            AppleTransaction(expires: Now.AddDays(30), revoked: Now.AddHours(-1)), "br.com.conora.app", null, Now).Entitled);
        Assert.False(StoreReceiptValidator.ReadAppleTransaction(
            AppleTransaction(expires: Now.AddDays(-1)), "br.com.conora.app", null, Now).Entitled);
    }

    [Fact]
    public void Apple_transaction_for_another_app_or_environment_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => StoreReceiptValidator.ReadAppleTransaction(
            AppleTransaction(expires: Now.AddDays(30)), "br.com.outro.app", null, Now));
        Assert.Throws<InvalidOperationException>(() => StoreReceiptValidator.ReadAppleTransaction(
            AppleTransaction(expires: Now.AddDays(30)), "br.com.conora.app", "Production", Now));
    }

    [Fact]
    public void Apple_notification_with_forged_signature_is_rejected()
    {
        var reader = Reader(new FakeStoreReceiptValidator());
        Assert.Throws<StoreNotificationRejectedException>(() => reader.ReadApple("eyJhbGciOiJub25lIn0.eyJ4IjoxfQ.c2ln"));
        Assert.Throws<StoreNotificationRejectedException>(() => reader.ReadApple(null));
    }

    [Fact]
    public async Task Google_notification_reads_state_from_play_api()
    {
        var validator = new FakeStoreReceiptValidator
        {
            Receipt = new StoreReceipt("conora.family.monthly", "GPA.9", "tok-9", Now.AddDays(30), true, false, User)
        };
        var notice = await Reader(validator).ReadGoogleAsync(Rtdn("""
            {"packageName":"br.com.conora.app","subscriptionNotification":{"notificationType":2,"purchaseToken":"tok-9","subscriptionId":"conora.family.monthly"}}
            """));

        Assert.NotNull(notice);
        Assert.Equal(StoreBillingChannel.Google, notice!.Channel);
        Assert.True(notice.Receipt.Entitled);
        Assert.Equal("tok-9", notice.Receipt.SubscriptionId);
    }

    [Fact]
    public async Task Google_voided_purchase_ends_access()
    {
        var validator = new FakeStoreReceiptValidator
        {
            Receipt = new StoreReceipt("conora.family.monthly", "GPA.9", "tok-9", Now.AddDays(30), true, false, User)
        };
        var notice = await Reader(validator).ReadGoogleAsync(Rtdn("""
            {"packageName":"br.com.conora.app","voidedPurchaseNotification":{"purchaseToken":"tok-9","orderId":"GPA.9","productType":1}}
            """));

        Assert.False(notice!.Receipt.Entitled);
    }

    [Fact]
    public async Task Google_notification_for_another_package_or_test_ping()
    {
        var reader = Reader(new FakeStoreReceiptValidator());
        await Assert.ThrowsAsync<StoreNotificationRejectedException>(() => reader.ReadGoogleAsync(Rtdn("""
            {"packageName":"com.other","subscriptionNotification":{"notificationType":4,"purchaseToken":"x"}}
            """)));
        await Assert.ThrowsAsync<StoreNotificationRejectedException>(() => reader.ReadGoogleAsync("not-base64!"));
        Assert.Null(await reader.ReadGoogleAsync(Rtdn("""
            {"packageName":"br.com.conora.app","testNotification":{"version":"1.0"}}
            """)));
    }

    [Fact]
    public void Store_period_uses_store_expiry_and_ignores_stale_notices()
    {
        var sub = WorkspaceSubscription.FromStore(PlanKind.IndividualMonthly, Now.AddDays(30), "app-store", "conora.individual.monthly", "orig-1");
        Assert.True(sub.IsWritable(Now));
        Assert.Equal(Now.AddDays(30), sub.ExpiresAt);

        // Older notice for the same store subscription arrives late: expiry is kept.
        sub.ApplyStorePeriod(PlanKind.IndividualMonthly, Now.AddDays(1), "app-store", "conora.individual.monthly", "orig-1");
        Assert.Equal(Now.AddDays(30), sub.ExpiresAt);

        // Upgrade to a family tier through the store.
        sub.ApplyStorePeriod(PlanKind.FamilyPlusYearly, Now.AddYears(1), "app-store", "conora.family.yearly", "orig-1");
        Assert.Equal(PlanTier.FamilyPlus, sub.Tier);

        sub.EndFromStore(Now);
        Assert.False(sub.IsWritable(Now));
        Assert.Equal(SubscriptionStatus.Expired, sub.EffectiveStatus(Now));

        // Resubscribing after the end reactivates the plan.
        sub.ApplyStorePeriod(PlanKind.IndividualMonthly, Now.AddDays(30), "app-store", "conora.individual.monthly", "orig-1");
        Assert.True(sub.IsWritable(Now));
    }

    private static StoreNotificationReader Reader(IStoreReceiptValidator validator) =>
        new(validator, Options.Create(new StoreBillingOptions()));

    private static string Rtdn(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json.Trim()));

    private static JsonElement AppleTransaction(DateTime expires, DateTime? revoked = null)
    {
        var revocation = revoked is DateTime r ? $", \"revocationDate\": {new DateTimeOffset(r).ToUnixTimeMilliseconds()}" : "";
        using var doc = JsonDocument.Parse($$"""
            {
              "environment": "Sandbox",
              "bundleId": "br.com.conora.app",
              "productId": "conora.individual.monthly",
              "transactionId": "2000000002",
              "originalTransactionId": "2000000001",
              "appAccountToken": "{{User}}",
              "expiresDate": {{new DateTimeOffset(expires).ToUnixTimeMilliseconds()}}{{revocation}}
            }
            """);
        return doc.RootElement.Clone();
    }
}
