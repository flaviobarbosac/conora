using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

/// <summary>
/// <paramref name="InstallmentCount"/> splits <paramref name="Amount"/> into monthly entries (each with its own competence).
/// <paramref name="RepeatMonths"/> repeats the same amount monthly (recurrence).
/// </summary>
public sealed record CreateEntryRequest(
    EntryType Type,
    decimal Amount,
    DateTime OccurredAt,
    string Description,
    string? CompetenceYm = null,
    Guid? AccountId = null,
    Guid? ContraAccountId = null,
    Guid? CategoryId = null,
    Guid? IncomeSourceId = null,
    Guid? LifeProjectId = null,
    Guid? MemberId = null,
    int? InstallmentCount = null,
    int? RepeatMonths = null,
    bool ConfirmDuplicate = false);

public sealed record UpdateEntryRequest(
    decimal Amount,
    DateTime OccurredAt,
    string Description,
    string? CompetenceYm = null,
    Guid? AccountId = null,
    Guid? ContraAccountId = null,
    Guid? CategoryId = null,
    Guid? IncomeSourceId = null,
    Guid? MemberId = null,
    bool ConfirmDuplicate = false);

public sealed record EntryResponse(
    Guid Id,
    EntryType Type,
    decimal Amount,
    DateTime OccurredAt,
    string CompetenceYm,
    Guid? AccountId,
    Guid? ContraAccountId,
    Guid? CategoryId,
    Guid? IncomeSourceId,
    Guid? CreditCardId,
    Guid? LifeProjectId,
    Guid? MemberId,
    string Description,
    string? RecurrenceKey,
    int? InstallmentNumber,
    int? InstallmentCount);

public sealed record EntryFilter(
    string? CompetenceYm = null,
    EntryType? Type = null,
    Guid? CategoryId = null,
    Guid? AccountId = null,
    string? Search = null,
    int Skip = 0,
    int Take = 50);

public sealed record CategorySuggestionResponse(Guid? CategoryId, string? CategoryName);
