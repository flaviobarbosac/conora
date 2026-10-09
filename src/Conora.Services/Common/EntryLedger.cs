using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Repository.Interface;

namespace Conora.Services.Common;

/// <summary>Applies (sign = 1) or reverses (sign = -1) an entry's effect on account balances.</summary>
internal static class EntryLedger
{
    public static async Task ApplyAsync(IFinanceRepository repo, Entry entry, int sign, CancellationToken ct)
    {
        foreach (var (accountId, delta) in entry.BalanceEffects())
        {
            var account = await repo.GetAsync<Account>(accountId, ct)
                          ?? throw new NotFoundException("Conta", accountId);
            account.ApplyDelta(delta * sign);
        }
    }
}
