using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

/// <summary>Optional CSV column mapping; header names or zero-based indexes as text.</summary>
public sealed record ImportMapping(string? DateColumn = null, string? AmountColumn = null, string? DescriptionColumn = null, char? Delimiter = null);

public sealed record ImportPreviewRequest(
    string FileName,
    ImportFormat Format,
    string Content,
    string? CompetenceYm = null,
    ImportMapping? Mapping = null);

public sealed record ImportRowResponse(
    Guid Id,
    DateTime Date,
    decimal Amount,
    string Description,
    bool IsDuplicate,
    bool WillImport,
    ImportRowStatus RowStatus,
    Guid? SuggestedCategoryId,
    string? SuggestedCategoryName,
    Guid? EntryId,
    int SortIndex);

public sealed record ImportDetectedAccountResponse(
    string? BankCode,
    string? Agency,
    string? AccountNumber,
    string? CheckDigit,
    string? BankName);

public sealed record ImportPreviewResponse(
    Guid BatchId,
    string FileName,
    ImportFormat Format,
    ImportStatus Status,
    string CompetenceYm,
    int RowCount,
    int DuplicateCount,
    int PendingCount,
    int DeferredCount,
    int CommittedCount,
    int IgnoredCount,
    Guid? AccountId,
    bool AccountMatched,
    bool AccountNeedsCreate,
    ImportDetectedAccountResponse? DetectedAccount,
    IReadOnlyList<ImportRowResponse> Rows);

public sealed record SetImportRowRequest(bool WillImport);

public sealed record CommitImportRequest(Guid AccountId, Guid? DefaultExpenseCategoryId = null, Guid? DefaultIncomeCategoryId = null);

public sealed record CommitImportResponse(Guid BatchId, int Imported, int Skipped);

public sealed record BindImportAccountRequest(Guid? AccountId = null, bool CreateIfMissing = false, string? Name = null, AccountKind? Kind = null);

public sealed record ClassifyImportRowRequest(Guid CategoryId, bool ConfirmDuplicate = false);

public sealed record ImportReplaceSummaryResponse(int AlreadyCommitted, int ToClassify);

public sealed record ImportCurrentRowResponse(
    Guid BatchId,
    ImportRowResponse Row,
    int Position,
    int TotalOpen,
    int DeferredCount,
    int CommittedCount,
    int TotalRows,
    bool RequiresDuplicateConfirm);

public sealed record ImportReportResponse(
    Guid BatchId,
    string CompetenceYm,
    string FileName,
    int PendingCount,
    int DeferredCount,
    int CommittedCount,
    int IgnoredCount,
    IReadOnlyList<ImportRowResponse> Pending,
    IReadOnlyList<ImportRowResponse> Deferred,
    IReadOnlyList<ImportRowResponse> Committed,
    IReadOnlyList<ImportRowResponse> Ignored);
