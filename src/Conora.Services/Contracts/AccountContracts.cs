using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record CreateAccountRequest(string Name, AccountKind Kind, decimal OpeningBalance = 0);

public sealed record UpdateAccountRequest(string Name, AccountKind Kind);

public sealed record ArchiveAccountRequest(bool Archived = true);

public sealed record AccountResponse(Guid Id, string Name, AccountKind Kind, decimal Balance, bool IsArchived);

public sealed record TransferRequest(
    Guid FromAccountId, Guid ToAccountId, decimal Amount, DateTime OccurredAt, string? Description = null);
