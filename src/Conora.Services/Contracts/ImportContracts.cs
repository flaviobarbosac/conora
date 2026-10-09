using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

/// <summary>Optional CSV column mapping; header names or zero-based indexes as text.</summary>
public sealed record ImportMapping(string? DateColumn = null, string? AmountColumn = null, string? DescriptionColumn = null, char? Delimiter = null);

public sealed record ImportPreviewRequest(string FileName, ImportFormat Format, string Content, ImportMapping? Mapping = null);

public sealed record ImportRowResponse(Guid Id, DateTime Date, decimal Amount, string Description, bool IsDuplicate, bool WillImport);

public sealed record ImportPreviewResponse(
    Guid BatchId,
    string FileName,
    ImportFormat Format,
    ImportStatus Status,
    int RowCount,
    int DuplicateCount,
    IReadOnlyList<ImportRowResponse> Rows);

public sealed record SetImportRowRequest(bool WillImport);

public sealed record CommitImportRequest(Guid AccountId, Guid? DefaultExpenseChartAccountId = null, Guid? DefaultIncomeChartAccountId = null);

public sealed record CommitImportResponse(Guid BatchId, int Imported, int Skipped);
