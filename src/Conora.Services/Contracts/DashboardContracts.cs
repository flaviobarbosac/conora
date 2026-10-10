namespace Conora.Services.Contracts;

/// <summary>Severity: Ok | Attention (>=70%) | Limit (=100%) | Exceeded (>100%) | Info.</summary>
public sealed record AlertResponse(string Code, string Severity, string Message, Guid? CategoryId = null, decimal? Percent = null);

public sealed record DashboardResponse(
    string CompetenceYm,
    bool IsClosed,
    decimal IncomeTotal,
    decimal ReceivedIncome,
    decimal ExpenseTotal,
    decimal Result,
    decimal ContributionsTotal,
    decimal CardPurchasesTotal,
    decimal ProjectContributionsTotal,
    decimal AccountsBalance,
    IReadOnlyList<AlertResponse> Alerts);

public sealed record CategoryTotalResponse(Guid? CategoryId, string CategoryName, decimal Amount);

public sealed record MonthlyReportResponse(
    DashboardResponse Summary,
    IReadOnlyList<CategoryTotalResponse> ByAccount,
    string PreviousYm,
    decimal PreviousExpenseTotal,
    decimal ExpenseDelta,
    decimal PreviousResult);

public sealed record ExportFile(string FileName, string ContentType, byte[] Content);
