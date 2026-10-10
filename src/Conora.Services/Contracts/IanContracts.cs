namespace Conora.Services.Contracts;

public sealed record IanAskRequest(string Question, string? CompetenceYm = null);

/// <summary>
/// Chat reply. When <see cref="CanOpenReport"/> is true, <see cref="Query"/> opens the static report page.
/// </summary>
public sealed record IanAskResponse(
    bool Available,
    string Answer,
    bool CanOpenReport,
    IanReportQuery? Query = null,
    IReadOnlyList<string>? Choices = null);

/// <summary>Analisar: user picks chart-of-accounts categories + period.</summary>
public sealed record IanAnalyzeRequest(
    IReadOnlyList<Guid> CategoryIds,
    string FromYm,
    string ToYm);

/// <summary>
/// Resolved query the report page reloads via GET.
/// Focus: expense | income | null.
/// Analyze: CategoryIds (comma-separated), optional ChartKind + Insight from the picker flow.
/// </summary>
public sealed record IanReportQuery(
    string Kind,
    string? CompetenceYm = null,
    string? FromYm = null,
    string? ToYm = null,
    string? Subject = null,
    string? MatchKind = null,
    Guid? MatchId = null,
    string? Focus = null,
    string? CategoryIds = null,
    string? ChartKind = null,
    string? Insight = null);

public sealed record IanTableColumn(string Key, string Label);

/// <summary>One table row. <see cref="Amount"/> feeds the chart when present.</summary>
public sealed record IanTableRow(string Key, IReadOnlyList<string> Cells, decimal? Amount = null);

public sealed record IanReportTable(
    IReadOnlyList<IanTableColumn> Columns,
    IReadOnlyList<IanTableRow> Rows);

public sealed record IanReportPoint(string Key, string Label, decimal Amount);

/// <summary>
/// Generic Ian report: dynamic table on top, chart below built from the same rows.
/// </summary>
public sealed record IanReportResponse(
    string Title,
    IanReportTable Table,
    string ChartKind,
    IReadOnlyList<IanReportPoint> Points,
    bool Found,
    string? Insight = null);
