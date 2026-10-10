namespace Conora.Domain.Services;

/// <summary>Picks the chart that best explains the resolved data shape.</summary>
public static class IanChartSelector
{
    public const string None = "none";
    public const string Bar = "bar";
    public const string Line = "line";
    public const string Pie = "pie";

    public const string KindSnapshot = "snapshot";
    public const string KindSeries = "series";
    public const string KindComposition = "composition";
    public const string KindAnalyze = "analyze";

    /// <summary>
    /// series → line (time). Everything else with 2+ slices → pie (parts / compare).
    /// Zero or one point → numbers only (none).
    /// </summary>
    public static string Choose(string kind, int pointCount)
    {
        if (pointCount <= 1)
            return None;

        return kind == KindSeries ? Line : Pie;
    }

    /// <summary>
    /// Safety net for the Analisar flow when Gemini is off or returns a bad chartKind.
    /// 1 account × several months → line; several accounts → pie of totals; else none.
    /// </summary>
    public static string ChooseAnalyze(int categoryCount, int monthCount, int chartPointCount)
    {
        if (chartPointCount <= 1)
            return None;

        if (categoryCount == 1 && monthCount >= 2)
            return Line;

        if (categoryCount >= 2)
            return Pie;

        return monthCount >= 2 ? Line : Bar;
    }
}
