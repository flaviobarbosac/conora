using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record CreateAccountRequest(
    string Name,
    AccountKind Kind,
    decimal OpeningBalance = 0,
    DateTime? OpeningBalanceAt = null,
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
    decimal OpeningBalance,
    DateTime OpeningBalanceAt,
    bool IsArchived,
    string? BankCode,
    string? Agency,
    string? AccountNumber,
    string? CheckDigit,
    string? BankName);

public sealed record BankOptionResponse(string Code, string Name);

/// <summary>Sum of account balances as of the last moment of <paramref name="EndYm"/>.</summary>
public sealed record CashBalanceResponse(string EndYm, decimal Balance);

public sealed record TransferRequest(
    Guid FromAccountId, Guid ToAccountId, decimal Amount, DateTime OccurredAt, string? Description = null);
