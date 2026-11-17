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
        Assert.Contains("R$ 299,90 por ano", html);
        Assert.Contains("R$ 399,90 por ano", html);
        Assert.Contains("R$ 499,90 por ano", html);
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
