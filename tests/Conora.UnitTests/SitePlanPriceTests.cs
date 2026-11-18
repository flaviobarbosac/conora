using Xunit;

namespace Conora.UnitTests;

public class SitePlanPriceTests
{
    [Fact]
    public void Public_site_shows_the_same_monthly_and_yearly_prices_as_the_catalog()
    {
        var html = File.ReadAllText(FindSite());

        Assert.Contains("data-m=\"29,90\"", html);
        Assert.Contains("data-m=\"39,90\"", html);
        Assert.Contains("data-m=\"49,90\"", html);
        Assert.Contains("R$ 299 por ano", html);
        Assert.Contains("R$ 399 por ano", html);
        Assert.Contains("R$ 499 por ano", html);
    }

    [Fact]
    public void Public_site_advertises_seven_day_trial_not_ninety()
    {
        var html = File.ReadAllText(FindSite());
        Assert.Contains("7 dias com tudo", html);
        Assert.Contains("Por 7 dias", html);
        Assert.DoesNotContain("90 dias com tudo", html);
        Assert.DoesNotContain("Por 90 dias", html);
    }

    private static string FindSite()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "infra", "public-site", "index.html");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Site público não encontrado a partir dos testes.");
    }
}
