using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class IanChartSelectorTests
{
    [Theory]
    [InlineData(IanChartSelector.KindSnapshot, 2, IanChartSelector.Pie)]
    [InlineData(IanChartSelector.KindSeries, 12, IanChartSelector.Line)]
    [InlineData(IanChartSelector.KindComposition, 5, IanChartSelector.Pie)]
    [InlineData(IanChartSelector.KindSnapshot, 1, IanChartSelector.None)]
    [InlineData(IanChartSelector.KindSeries, 0, IanChartSelector.None)]
    public void Choose_picks_chart_for_data_shape(string kind, int points, string expected)
        => Assert.Equal(expected, IanChartSelector.Choose(kind, points));

    [Theory]
    [InlineData(1, 10, 10, IanChartSelector.Line)]
    [InlineData(3, 1, 3, IanChartSelector.Pie)]
    [InlineData(2, 6, 2, IanChartSelector.Pie)]
    [InlineData(1, 1, 1, IanChartSelector.None)]
    public void ChooseAnalyze_picks_chart_for_picker_shape(int categories, int months, int points, string expected)
        => Assert.Equal(expected, IanChartSelector.ChooseAnalyze(categories, months, points));
}
