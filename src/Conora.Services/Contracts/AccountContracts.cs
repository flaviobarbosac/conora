using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record CreateAccountRequest(
    string Name,
    AccountKind Kind,
    decimal OpeningBalance = 0,
    string? BankCode = null,
    string? Agency = null,
    string? AccountNumber = null,
    string? CheckDigit = null);

public sealed record UpdateAccountRequest(
    string Name,
    AccountKind Kind,
    string? BankCode = null,
    string? Agency = null,
    string? AccountNumber = null,
    string? CheckDigit = null);

public sealed record ArchiveAccountRequest(bool Archived = true);

public sealed record AccountResponse(
    Guid Id,
    string Name,
    AccountKind Kind,
    decimal Balance,
    bool IsArchived,
    string? BankCode,
    string? Agency,
    string? AccountNumber,
    string? CheckDigit,
    string? BankName);

public sealed record BankOptionResponse(string Code, string Name);

public sealed record TransferRequest(
    Guid FromAccountId, Guid ToAccountId, decimal Amount, DateTime OccurredAt, string? Description = null);
