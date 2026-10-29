using Conora.Domain.Enums;
using Conora.Services.CategoryAccess;
using Xunit;

namespace Conora.UnitTests;

public class CategoryAccessPolicyTests
{
    private readonly CategoryAccessPolicyFactory _factory = new();

    [Fact]
    public void Full_access_allows_any_analytical()
    {
        var policy = _factory.ForFullAccess(true);
        Assert.True(policy.CanWriteAnalytical(null, CategorySection.Essential));
        Assert.True(policy.CanWriteAnalytical("ANY", CategorySection.Income));
    }

    [Fact]
    public void Free_tier_only_allows_catalog_codes()
    {
        var policy = _factory.ForFullAccess(false);
        Assert.True(policy.CanWriteAnalytical("ESS_RENT", CategorySection.Essential));
        Assert.True(policy.CanWriteAnalytical("inc_salary", CategorySection.Income));
        Assert.False(policy.CanWriteAnalytical("OTHER", CategorySection.Essential));
        Assert.False(policy.CanWriteAnalytical(" ", CategorySection.Essential));
        Assert.False(policy.CanWriteAnalytical(null, CategorySection.Essential));
    }
}
