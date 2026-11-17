using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Services;
using Conora.Services;
using Xunit;

namespace Conora.UnitTests;

public class PlanAccessTests
{
    private static readonly DateTime Created = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, AccessPhase.Trial, true)]
    [InlineData(89, AccessPhase.Trial, true)]
    [InlineData(90, AccessPhase.Blocked, false)]
    [InlineData(99, AccessPhase.Blocked, false)]
    [InlineData(100, AccessPhase.Blocked, false)]
    [InlineData(150, AccessPhase.Blocked, false)]
    public void Student_trial_then_plans_screen_without_plan(int days, AccessPhase expected, bool writable)
    {
        var state = AccessPolicy.Evaluate(Created, null, Created.AddDays(days), isTrainingStudent: true);

        Assert.Equal(expected, state.Phase);
        Assert.Equal(writable, state.IsWritable);
    }

    [Theory]
    [InlineData(0, AccessPhase.Trial, true)]
    [InlineData(6, AccessPhase.Trial, true)]
    [InlineData(7, AccessPhase.Blocked, false)]
    [InlineData(40, AccessPhase.Blocked, false)]
    public void Standard_trial_is_7_days_then_plans_screen(int days, AccessPhase expected, bool writable)
    {
        var state = AccessPolicy.Evaluate(Created, null, Created.AddDays(days), isTrainingStudent: false);

        Assert.Equal(expected, state.Phase);
        Assert.Equal(Created.AddDays(7), state.TrialEndsAt);
        Assert.Equal(writable, state.IsWritable);
    }

    [Fact]
    public void Console_can_move_the_tasting_end_and_grant_access()
    {
        var extended = AccessPolicy.Evaluate(
            Created, null, Created.AddDays(20), isTrainingStudent: false, trialEndsAt: Created.AddDays(30));
        Assert.Equal(AccessPhase.Trial, extended.Phase);

        var open = AccessPolicy.Evaluate(
            Created, null, Created.AddDays(20), isTrainingStudent: false, complimentaryIndefinite: true);
        Assert.Equal(AccessPhase.Granted, open.Phase);
        Assert.True(open.IsWritable);

        var until = Created.AddDays(40);
        var dated = AccessPolicy.Evaluate(
            Created, null, Created.AddDays(20), isTrainingStudent: false, complimentaryUntil: until);
        Assert.Equal(AccessPhase.Granted, dated.Phase);

        var finished = AccessPolicy.Evaluate(
            Created, null, until, isTrainingStudent: false, complimentaryUntil: until);
        Assert.Equal(AccessPhase.Blocked, finished.Phase);
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
    public void Active_plan_overrides_trial_age_and_expired_plan_stays_writable_free_tier()
    {
        var now = Created.AddDays(200);
        var active = WorkspaceSubscription.Activate(PlanKind.FamilyMonthly, now.AddDays(-5));
        var expired = WorkspaceSubscription.Activate(PlanKind.IndividualMonthly, now.AddMonths(-2));

        Assert.Equal(AccessPhase.Subscribed, AccessPolicy.Evaluate(Created, active, now).Phase);
        var expiredState = AccessPolicy.Evaluate(Created, expired, now);
        Assert.Equal(AccessPhase.Expired, expiredState.Phase);
        Assert.True(expiredState.IsWritable);
    }

    [Fact]
    public void Free_tier_allows_starter_categories_including_salary()
    {
        Assert.True(FreeTierLimits.AllowsCategoryCode("ESS_RENT"));
        Assert.True(FreeTierLimits.AllowsCategoryCode("ESS_POWER"));
        Assert.True(FreeTierLimits.AllowsCategoryCode("ESS_WATER"));
        Assert.True(FreeTierLimits.AllowsCategoryCode("ESS_SCHOOL"));
        Assert.True(FreeTierLimits.AllowsCategoryCode("INC_SALARY"));
        Assert.False(FreeTierLimits.AllowsCategoryCode("ESS_CONDO"));
        Assert.False(FreeTierLimits.AllowsCategoryCode("INC_13TH"));
        Assert.Equal(1, FreeTierLimits.MaxBankAccounts);
    }

    [Theory]
    [InlineData(PlanKind.IndividualMonthly, 29.90, PlanTier.Individual)]
    [InlineData(PlanKind.IndividualYearly, 299.90, PlanTier.Individual)]
    [InlineData(PlanKind.FamilyMonthly, 39.90, PlanTier.Family)]
    [InlineData(PlanKind.FamilyYearly, 399.90, PlanTier.Family)]
    [InlineData(PlanKind.FamilyPlusMonthly, 49.90, PlanTier.FamilyPlus)]
    [InlineData(PlanKind.FamilyPlusYearly, 499.90, PlanTier.FamilyPlus)]
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
        Assert.Equal(PlanTier.Individual, PlanService.FamilySeatTier(AccessPhase.Trial, PlanTier.FamilyPlus));
        Assert.Equal(PlanTier.Individual, PlanService.FamilySeatTier(AccessPhase.Granted, PlanTier.Family));
        Assert.Equal(PlanTier.Family, PlanService.FamilySeatTier(AccessPhase.Subscribed, PlanTier.Family));
        Assert.True(PlanService.HasFullAccess(new AccessSnapshot(
            AccessPolicy.Evaluate(Created, null, Created.AddDays(1), isTrainingStudent: false),
            null, false, false, false)));
        Assert.True(PlanService.HasFullAccess(new AccessSnapshot(
            AccessPolicy.Evaluate(Created, null, Created.AddDays(40), isTrainingStudent: false, complimentaryIndefinite: true),
            null, false, false, false)));
        Assert.False(PlanService.HasFullAccess(new AccessSnapshot(
            AccessPolicy.Evaluate(Created, null, Created.AddDays(8), isTrainingStudent: false),
            null, false, false, false)));
        Assert.Equal(1, PlanCatalog.MaxExtraMembers(PlanTier.Family));
        Assert.Equal(5, PlanCatalog.MaxExtraMembers(PlanTier.FamilyPlus));
    }
}
