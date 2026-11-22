using Conora.Domain.Entities;
using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class DreamProjectRuleTests
{
    private static readonly Guid EmergencyReserve = Guid.NewGuid();
    private static readonly Guid AnnualReserve = Guid.NewGuid();
    private static readonly Guid SystemTrip = Guid.NewGuid();
    private static readonly Guid UserCreated = Guid.NewGuid();
    private static readonly HashSet<Guid> ReserveIds = [EmergencyReserve, AnnualReserve];

    private static LifeProject Project(string name, Guid? categoryId, decimal accumulated)
    {
        var project = LifeProject.Create(name, 10_000m, new DateTime(2028, 6, 30), "2027-01", categoryId);
        if (accumulated > 0)
            project.ApplyContribution(accumulated);
        return project;
    }

    [Fact]
    public void Reserve_codes_are_the_emergency_and_annual_reserves()
        => Assert.Equal(["LIFE_RESERVE", "LIFE_ANNUAL"], DreamProjectRule.ReserveCategoryCodes);

    [Fact]
    public void Only_reserves_with_money_do_not_count()
    {
        var projects = new[]
        {
            Project("Reserva de emergência", EmergencyReserve, 26_862m),
            Project("IPVA e IPTU", AnnualReserve, 3_000m)
        };

        Assert.Null(DreamProjectRule.FirstFundedDream(projects, ReserveIds));
    }

    [Fact]
    public void Dream_in_a_system_category_counts()
    {
        var disney = Project("Viagem para a Disney", SystemTrip, 3_055m);

        Assert.Same(disney, DreamProjectRule.FirstFundedDream([disney], ReserveIds));
    }

    [Fact]
    public void Dream_in_a_user_created_category_counts()
    {
        var dream = Project("Estúdio de música", UserCreated, 500m);

        Assert.Same(dream, DreamProjectRule.FirstFundedDream([dream], ReserveIds));
    }

    [Fact]
    public void Reserve_and_dream_together_pick_the_dream()
    {
        var reserve = Project("Reserva de emergência", EmergencyReserve, 26_862m);
        var disney = Project("Viagem para a Disney", SystemTrip, 3_055m);

        Assert.Same(disney, DreamProjectRule.FirstFundedDream([reserve, disney], ReserveIds));
    }

    [Fact]
    public void Dream_without_money_does_not_count()
    {
        var disney = Project("Viagem para a Disney", SystemTrip, 0m);

        Assert.Null(DreamProjectRule.FirstFundedDream([disney], ReserveIds));
    }
}
