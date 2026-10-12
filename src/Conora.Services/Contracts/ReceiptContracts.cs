using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record ReceiptIntakeResponse(
    Guid Id,
    ReceiptChannel Channel,
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
    bool NeedsClassification);

public sealed record ClassifyReceiptRequest(string CategoryCode);

public sealed record ConfirmReceiptRequest(
    Guid? AccountId = null,
    Guid? CategoryId = null,
    decimal? Amount = null,
    DateTime? OccurredAt = null,
    string? Description = null,
    bool ConfirmDuplicate = false);

public sealed record CreateReceiptFromBytesRequest(
    ReceiptChannel Channel,
    byte[] ImageBytes,
    string ContentType,
    string? PhoneE164 = null);
