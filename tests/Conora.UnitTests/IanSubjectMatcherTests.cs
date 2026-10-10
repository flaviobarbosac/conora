using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class IanSubjectMatcherTests
{
    [Fact]
    public void Find_prefers_category_then_description_then_account()
    {
        var categoryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var entryId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var accountId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var matches = IanSubjectMatcher.Find(
            "edp",
            categories: [(categoryId, "Energia EDP")],
            entryDescriptions: [(entryId, "Conta EDP março")],
            accounts: [(accountId, "EDP Poupança")]);

        Assert.Equal(3, matches.Count);
        Assert.Equal(IanSubjectMatcher.MatchCategory, matches[0].MatchKind);
        Assert.Equal(categoryId, matches[0].Id);
        Assert.Equal(IanSubjectMatcher.MatchDescription, matches[1].MatchKind);
        Assert.Equal(IanSubjectMatcher.MatchAccount, matches[2].MatchKind);
    }

    [Fact]
    public void Find_ignores_accents_and_case()
    {
        var id = Guid.NewGuid();
        var matches = IanSubjectMatcher.Find(
            "água",
            categories: [(id, "Agua e esgoto")],
            entryDescriptions: [],
            accounts: []);

        Assert.Single(matches);
        Assert.Equal(id, matches[0].Id);
    }

    [Fact]
    public void Find_returns_empty_when_nothing_matches()
    {
        var matches = IanSubjectMatcher.Find(
            "xyz",
            categories: [(Guid.NewGuid(), "Luz")],
            entryDescriptions: [(Guid.NewGuid(), "Mercado")],
            accounts: [(Guid.NewGuid(), "Nubank")]);

        Assert.Empty(matches);
    }
}
