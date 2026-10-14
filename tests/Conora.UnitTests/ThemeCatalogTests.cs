using Conora.Domain.Catalog;
using Xunit;

namespace Conora.UnitTests;

public class ThemeCatalogTests
{
    [Theory]
    [InlineData("clareza")]
    [InlineData("noite")]
    [InlineData("contraste")]
    [InlineData("salvia")]
    [InlineData("lavanda")]
    [InlineData("terracota")]
    public void All_themes_are_known(string themeId)
    {
        Assert.True(ThemeCatalog.IsKnown(themeId));
    }

    [Fact]
    public void Normalize_maps_unknown_to_clareza()
    {
        Assert.Equal(ThemeCatalog.Clareza, ThemeCatalog.Normalize("pink"));
        Assert.Equal(ThemeCatalog.Salvia, ThemeCatalog.Normalize("Salvia"));
    }
}
