using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class FamilySuccessionRulesTests
{
    private static DateTime Brazil(int year, int month, int day, int hour = 12) =>
        new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc).AddHours(3);

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Request_inside_day_1_to_5_is_allowed(int day)
    {
        Assert.True(FamilySuccessionRules.CanRequest(Brazil(2026, 10, day)));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(15)]
    [InlineData(31)]
    public void Request_outside_day_1_to_5_is_rejected(int day)
    {
        Assert.False(FamilySuccessionRules.CanRequest(Brazil(2026, 10, day)));
        Assert.DoesNotContain("48", FamilySuccessionRules.OutsideWindowMessage);
    }

    [Fact]
    public void Cancellation_replaces_every_budget_from_the_handover_month_forward_before_she_subscribes()
    {
        var wife = new List<FamilySuccessionRules.PlannedItem>
        {
            new("2026-09", "aluguel", 50),
            new("2026-10", "aluguel", 100),
            new("2026-10", "comida", 40),
            new("2026-11", "viagem", 50),
            new("2026-12", "presente", 20)
        };
        var husband = new List<FamilySuccessionRules.PlannedItem>
        {
            new("2026-09", "aluguel", 999),
            new("2026-10", "aluguel", 900),
            new("2026-10", "escola", 300),
            new("2026-11", "viagem", 800),
            new("2026-11", "seguro", 200),
            new("2026-12", "escola", 300)
        };

        var migrated = FamilySuccessionRules.MigrateBudgets(wife, husband, "2026-10");

        Assert.Equal([new FamilySuccessionRules.PlannedItem("2026-09", "aluguel", 50)], migrated.Where(item => item.Month == "2026-09").ToArray());
        Assert.Equal(
            [
                new FamilySuccessionRules.PlannedItem("2026-10", "aluguel", 900),
                new FamilySuccessionRules.PlannedItem("2026-10", "escola", 300)
            ],
            migrated.Where(item => item.Month == "2026-10").ToArray());
        Assert.Equal(
            [
                new FamilySuccessionRules.PlannedItem("2026-11", "seguro", 200),
                new FamilySuccessionRules.PlannedItem("2026-11", "viagem", 800)
            ],
            migrated.Where(item => item.Month == "2026-11").ToArray());
        Assert.Equal([new FamilySuccessionRules.PlannedItem("2026-12", "escola", 300)], migrated.Where(item => item.Month == "2026-12").ToArray());
        Assert.DoesNotContain(migrated, item => item.Month == "2026-10" && item.Key == "comida");
        Assert.DoesNotContain(migrated, item => item.Month == "2026-11" && item.Amount == 50);
        Assert.Contains(husband, item => item.Month == "2026-09" && item.Amount == 999);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void Successor_subscription_on_day_6_or_7_restores_the_group(int day)
    {
        var cancelled = Brazil(2026, 10, 5, 10);
        var subscribed = Brazil(2026, 10, day, 18);

        Assert.Equal(
            FamilySuccessionRules.Outcome.Restore,
            FamilySuccessionRules.Decide(cancelled, subscribed, successorSubscribed: true));
    }

    [Fact]
    public void Successor_subscription_on_day_8_dissolves_and_does_not_restore_later()
    {
        var cancelled = Brazil(2026, 10, 5, 10);
        var tooLate = Brazil(2026, 10, 8, 1);

        Assert.Equal(
            FamilySuccessionRules.Outcome.Dissolve,
            FamilySuccessionRules.Decide(cancelled, tooLate, successorSubscribed: true));

        var group = new GroupPicture();
        group.Apply(FamilySuccessionRules.Outcome.Dissolve);
        group.Apply(FamilySuccessionRules.Decide(cancelled, tooLate.AddDays(2), successorSubscribed: true) == FamilySuccessionRules.Outcome.Restore
            ? FamilySuccessionRules.Outcome.Restore
            : FamilySuccessionRules.Outcome.Dissolve);

        Assert.False(group.Active);
        Assert.Empty(group.Members);
        Assert.Empty(group.Invites);
        Assert.False(group.CanAutoRestore);
    }

    [Fact]
    public void Public_text_does_not_mention_the_internal_extension()
    {
        var text = FamilySuccessionRules.OutsideWindowMessage + FamilySuccessionRules.PublicRestoreMessage;
        Assert.DoesNotContain("48", text);
        Assert.DoesNotContain("dia 6", text);
        Assert.DoesNotContain("dia 7", text);
    }

    private sealed class GroupPicture
    {
        public bool Active { get; private set; } = true;
        public List<string> Members { get; } = ["marido", "esposa"];
        public List<string> Invites { get; } = ["pendente"];
        public bool CanAutoRestore { get; private set; } = true;

        public void Apply(FamilySuccessionRules.Outcome outcome)
        {
            if (!Active || outcome != FamilySuccessionRules.Outcome.Dissolve)
                return;
            Active = false;
            Members.Clear();
            Invites.Clear();
            CanAutoRestore = false;
        }
    }
}
