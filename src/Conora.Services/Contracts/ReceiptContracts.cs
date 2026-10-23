using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record SimilarSupplierResponse(
    Guid Id,
    string? Cnpj,
    string Name,
    string? DefaultCategoryCode,
    string MatchReason);

public sealed record ReceiptIntakeResponse(
    Guid Id,
    ReceiptChannel Channel,
    ReceiptPurpose Purpose,
    ReceiptIntakeStatus Status,
    string? ExtractedCnpj,
    string? ExtractedName,
    decimal? ExtractedAmount,
    DateTime? ExtractedOccurredAt,
    Guid? SupplierId,
    string? ProposedCategoryCode,
    Guid? ProposedCategoryId,
    string? ProposedCategoryName,
    Guid? SelectedBankAccountId,
    string? FailureReason,
    bool NeedsClassification,
    IReadOnlyList<SimilarSupplierResponse> SimilarSuppliers,
    string? BudgetCompetenceYm = null);

public sealed record ClassifyReceiptRequest(string CategoryCode);

public sealed record LinkSupplierRequest(Guid SupplierId);

public sealed record ConfirmReceiptRequest(
    Guid? AccountId = null,
    Guid? CategoryId = null,
    decimal? Amount = null,
    DateTime? OccurredAt = null,
    string? Description = null,
    bool ConfirmDuplicate = false);

public sealed record BudgetBoletoConfirmResponse(
    Guid IntakeId,
    string CompetenceYm,
    Guid CategoryId,
    string? CategoryName,
    decimal PlannedAmount,
    int? ExpenseDay);

public sealed record CreateReceiptFromBytesRequest(
    ReceiptChannel Channel,
    byte[] ImageBytes,
    string ContentType,
    string? PhoneE164 = null,
    ReceiptPurpose Purpose = ReceiptPurpose.Expense);
