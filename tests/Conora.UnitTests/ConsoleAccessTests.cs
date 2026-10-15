using Conora.Domain.Catalog;
using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class ConsoleAccessTests
{
    [Theory]
    [InlineData("flaviobarbosa.vix@gmail.com")]
    [InlineData("FLAVIOBARBOSA.VIX@GMAIL.COM")]
    [InlineData(" leolelcampos2009@gmail.com ")]
    public void Allows_operators(string email) => Assert.True(ConsoleAccess.IsAllowed(email));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("outra.pessoa@gmail.com")]
    public void Rejects_everyone_else(string? email) => Assert.False(ConsoleAccess.IsAllowed(email));

    [Fact]
    public void Streak_counts_only_consecutive_months_inside_the_ceiling()
    {
        var user = Guid.NewGuid();
        var closed = new List<(Guid UserId, string CompetenceYm)>
        {
            (user, "2026-01"),
            (user, "2026-02"),
            (user, "2026-03"),
            (user, "2026-05")
        };
        var cells = new Dictionary<(Guid, string), (decimal Planned, decimal Spent)>
        {
            [(user, "2026-01")] = (100m, 80m),
            [(user, "2026-02")] = (100m, 90m),
            [(user, "2026-03")] = (100m, 100m),
            [(user, "2026-05")] = (100m, 10m)
        };

        Assert.Equal(1, MethodAdherence.CountUsersWithStreak(closed, cells));
        Assert.True(MethodAdherence.IsWithin(100m, 100m));
        Assert.False(MethodAdherence.IsWithin(100m, 101m));
        Assert.False(MethodAdherence.IsWithin(0m, 0m));
    }
}
