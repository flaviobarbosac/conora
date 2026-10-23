using Conora.Domain.Enums;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Infrastructure.Billing;
using Conora.Services.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Conora.UnitTests;

public class StoreSubscriptionBillingTests
{
    [Theory]
    [InlineData(StoreProductIds.IndividualMonthly, PlanKind.IndividualMonthly, 49.90)]
    [InlineData(StoreProductIds.IndividualYearly, PlanKind.IndividualYearly, 499.00)]
    [InlineData(StoreProductIds.DuoMonthly, PlanKind.FamilyMonthly, 59.90)]
    [InlineData(StoreProductIds.DuoYearly, PlanKind.FamilyYearly, 599.00)]
    [InlineData(StoreProductIds.FamilyMonthly, PlanKind.FamilyPlusMonthly, 79.00)]
    [InlineData(StoreProductIds.FamilyYearly, PlanKind.FamilyPlusYearly, 790.00)]
    public void TryMapProduct_maps_conora_skus(string productId, PlanKind expected, double price)
    {
        var sut = CreateSut();
        Assert.True(sut.TryMapProduct(productId, out var plan));
        Assert.Equal(expected, plan);
        Assert.Equal((decimal)price, sut.PriceOfProduct(productId));
    }

    [Fact]
    public void TryMapProduct_rejects_unknown_and_has_no_student_sku()
    {
        var sut = CreateSut();
        Assert.False(sut.TryMapProduct("clampfy.essencial.monthly", out _));
        Assert.False(sut.TryMapProduct("conora.student.monthly", out _));
        Assert.Null(sut.PriceOfProduct("unknown"));
    }

    [Theory]
    [InlineData(true, 90)]
    [InlineData(false, 30)]
    public void Trial_days_come_from_profile_not_store_product(bool student, int days)
    {
        Assert.Equal(days, AccessPolicy.TrialDaysFor(student));
    }

    [Fact]
    public void ExternalReference_hashes_long_transaction_ids()
    {
        var shortRef = StoreSubscriptionBilling.ExternalReference(
            StoreBillingChannel.Google, "tok-1");
        Assert.Equal("loja:1:tok-1", shortRef);

        var longId = new string('x', 200);
        var hashed = StoreSubscriptionBilling.ExternalReference(StoreBillingChannel.Apple, longId);
        Assert.StartsWith("loja:2:sha256:", hashed);
        Assert.True(hashed.Length <= 120);
    }

    private static StoreSubscriptionBilling CreateSut() =>
        new(
            null!,
            new FakeStoreReceiptValidator(),
            null!,
            Options.Create(new StoreBillingOptions()),
            NullLogger<StoreSubscriptionBilling>.Instance);
}

internal sealed class FakeStoreReceiptValidator : IStoreReceiptValidator
{
    public StoreReceipt? Receipt { get; set; }

    public Task<StoreReceipt> ValidateAsync(
        StoreBillingChannel channel, string productId, string transactionId, string purchaseToken, CancellationToken ct = default)
        => Task.FromResult(Receipt ?? throw new InvalidOperationException("A loja não confirmou esta compra."));

    public Task<StoreReceipt> FetchGoogleSubscriptionAsync(string purchaseToken, CancellationToken ct = default)
        => Task.FromResult(Receipt ?? throw new InvalidOperationException("A Google Play não confirmou esta compra."));
}
