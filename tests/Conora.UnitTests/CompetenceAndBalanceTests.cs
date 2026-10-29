using Conora.Domain.Exceptions;
using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class CompetenceAndBalanceTests
{
    [Theory]
    [InlineData("2026-10", true)]
    [InlineData("2026-1", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsValid_checks_yyyy_MM(string? ym, bool expected)
        => Assert.Equal(expected, Competence.IsValid(ym));

    [Fact]
    public void Require_AddMonths_Compare_and_Range()
    {
        Assert.Equal("2026-10", Competence.Require("2026-10"));
        Assert.Throws<ValidationException>(() => Competence.Require("bad"));
        Assert.Equal("2027-01", Competence.AddMonths("2026-12", 1));
        Assert.True(Competence.Compare("2026-01", "2026-02") < 0);
        Assert.Equal(
            new[] { "2026-01", "2026-02", "2026-03" },
            Competence.RangeInclusive("2026-01", "2026-03"));
        Assert.Throws<ValidationException>(() => Competence.RangeInclusive("2026-03", "2026-01"));
    }

    [Fact]
    public void Start_EndExclusive_and_ToUtc()
    {
        var start = Competence.Start("2026-02");
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), Competence.EndExclusive("2026-02"));
        var local = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Local);
        Assert.Equal(DateTimeKind.Utc, Competence.ToUtc(local).Kind);
        Assert.Equal(DateTimeKind.Utc, Competence.ToUtc(DateTime.UtcNow).Kind);
    }

    [Fact]
    public void MonthBalance_subtracts_outflows_from_received_income()
        => Assert.Equal(1000m, MonthBalance.FromReceived(2000m, 100m, 200m, 500m, 200m));

    [Theory]
    [InlineData(100, 50, true)]
    [InlineData(100, 100, true)]
    [InlineData(100, 101, false)]
    [InlineData(0, 0, false)]
    public void MethodAdherence_IsWithin(decimal planned, decimal spent, bool expected)
        => Assert.Equal(expected, MethodAdherence.IsWithin(planned, spent));

    [Fact]
    public void MethodAdherence_counts_users_with_streak()
    {
        var user = Guid.NewGuid();
        var closed = new List<(Guid, string)>
        {
            (user, "2026-01"),
            (user, "2026-02"),
            (user, "2026-03"),
        };
        var cells = new Dictionary<(Guid, string), (decimal, decimal)>
        {
            [(user, "2026-01")] = (100m, 80m),
            [(user, "2026-02")] = (100m, 90m),
            [(user, "2026-03")] = (100m, 95m),
        };
        Assert.Equal(1, MethodAdherence.CountUsersWithStreak(closed, cells, length: 3));
        Assert.Equal(0, MethodAdherence.CountUsersWithStreak([], cells));
    }

    [Fact]
    public void GeminiQuota_constants()
    {
        Assert.Equal(50, GeminiQuota.MaxCallsPerHour);
        Assert.Contains("hora", GeminiQuota.HourlyLimitMessage);
    }

    [Fact]
    public void AccessPolicy_DaysUntil_never_negative()
    {
        var now = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(0, AccessPolicy.DaysUntil(now.AddDays(-1), now));
        Assert.Equal(5, AccessPolicy.DaysUntil(now.AddDays(5), now));
        Assert.Equal(30, AccessPolicy.TrialDaysFor(false));
        Assert.Equal(90, AccessPolicy.TrialDaysFor(true));
    }
}
