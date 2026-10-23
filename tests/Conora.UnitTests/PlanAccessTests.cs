using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class PlanAccessTests
{
    private static readonly DateTime Created = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, AccessPhase.Trial)]
    [InlineData(89, AccessPhase.Trial)]
    [InlineData(90, AccessPhase.Grace)]
    [InlineData(99, AccessPhase.Grace)]
    [InlineData(100, AccessPhase.Blocked)]
    [InlineData(150, AccessPhase.Blocked)]
    public void Student_trial_lifecycle_without_plan(int days, AccessPhase expected)
    {
        var state = AccessPolicy.Evaluate(Created, null, Created.AddDays(days), isTrainingStudent: true);

        Assert.Equal(expected, state.Phase);
        Assert.Equal(expected == AccessPhase.Trial, state.IsWritable);
    }

    [Theory]
    [InlineData(0, AccessPhase.Trial)]
    [InlineData(29, AccessPhase.Trial)]
    [InlineData(30, AccessPhase.Grace)]
    [InlineData(39, AccessPhase.Grace)]
    [InlineData(40, AccessPhase.Blocked)]
    public void Standard_trial_is_30_days_then_10_grace(int days, AccessPhase expected)
    {
        var state = AccessPolicy.Evaluate(Created, null, Created.AddDays(days), isTrainingStudent: false);

        Assert.Equal(expected, state.Phase);
        Assert.Equal(Created.AddDays(30), state.TrialEndsAt);
        Assert.Equal(Created.AddDays(40), state.BlockAt);
    }

    [Fact]
    public void Accounts_older_than_launch_start_trial_on_launch_floor()
    {
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var state = AccessPolicy.Evaluate(old, null, AccessPolicy.TrialFloorUtc.AddDays(10), isTrainingStudent: true);

        Assert.Equal(AccessPhase.Trial, state.Phase);
        Assert.Equal(AccessPolicy.TrialFloorUtc.AddDays(90), state.TrialEndsAt);
    }

    [Fact]
    public void Active_plan_overrides_trial_age_and_expired_plan_is_read_only()
    {
        var now = Created.AddDays(200);
        var active = WorkspaceSubscription.Activate(PlanKind.FamilyMonthly, now.AddDays(-5));
        var expired = WorkspaceSubscription.Activate(PlanKind.IndividualMonthly, now.AddMonths(-2));

        Assert.Equal(AccessPhase.Subscribed, AccessPolicy.Evaluate(Created, active, now).Phase);
        Assert.Equal(AccessPhase.Expired, AccessPolicy.Evaluate(Created, expired, now).Phase);
    }

    [Theory]
    [InlineData(PlanKind.IndividualMonthly, 49.90, PlanTier.Individual)]
    [InlineData(PlanKind.IndividualYearly, 499.00, PlanTier.Individual)]
    [InlineData(PlanKind.FamilyMonthly, 59.90, PlanTier.Family)]
    [InlineData(PlanKind.FamilyYearly, 599.00, PlanTier.Family)]
    [InlineData(PlanKind.FamilyPlusMonthly, 79.00, PlanTier.FamilyPlus)]
    [InlineData(PlanKind.FamilyPlusYearly, 790.00, PlanTier.FamilyPlus)]
    public void Catalog_prices_yearly_with_two_free_months(PlanKind kind, double price, PlanTier tier)
    {
        Assert.Equal((decimal)price, PlanCatalog.PriceOf(kind));
        Assert.Equal(tier, PlanCatalog.TierOf(kind));
    }

    [Fact]
    public void Yearly_plan_lasts_one_year_and_legacy_kinds_still_load()
    {
        var now = Created;
        var yearly = WorkspaceSubscription.Activate(PlanKind.FamilyPlusYearly, now);

        Assert.Equal(now.AddYears(1), yearly.ExpiresAt);
        Assert.Equal(PlanTier.Individual, PlanCatalog.TierOf(PlanKind.Monthly1490));
        Assert.False(PlanCatalog.IsSellable(PlanKind.Yearly14990));
        Assert.Equal(0, PlanCatalog.MaxExtraMembers(PlanTier.Individual));
        Assert.Equal(1, PlanCatalog.MaxExtraMembers(PlanTier.Family));
        Assert.Equal(4, PlanCatalog.MaxExtraMembers(PlanTier.FamilyPlus));
    }
}
