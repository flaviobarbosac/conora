using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class ConsultantMonthScopeTests
{
    [Fact]
    public void Shared_month_is_open_and_another_month_is_refused()
    {
        var scope = new ConsultantMonthScope();
        scope.Activate(Guid.NewGuid(), "2026-08", "Leomar Campos");

        scope.Ensure("2026-08");
        scope.EnsureAll(["2026-08"]);

        var error = Assert.Throws<ForbiddenException>(() => scope.Ensure("2026-12"));
        Assert.Equal("Fora do mês compartilhado.", error.Message);
        Assert.Throws<ForbiddenException>(() => scope.EnsureAll(["2026-08", "2026-09"]));
    }

    [Fact]
    public void Without_a_share_the_scope_does_not_block()
    {
        var scope = new ConsultantMonthScope();
        scope.Ensure("2026-01");
        scope.EnsureAll(["2026-02", "2026-03"]);
    }

    [Fact]
    public void Entry_keeps_the_consultant_as_author()
    {
        var entry = Entry.Create(Domain.Enums.EntryType.Expense, 10, new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc), "Mercado", "2026-08");
        var consultant = Guid.NewGuid();

        entry.StampActor(consultant);

        Assert.Equal(consultant, entry.ActorUsuarioId);
        Assert.Equal("2026-08", entry.CompetenceYm);
    }

    [Fact]
    public void Share_refuses_the_same_user_on_both_sides()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ValidationException>(() => MonthShare.Open(id, id, "2026-08"));
    }
}
