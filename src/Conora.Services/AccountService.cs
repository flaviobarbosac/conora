using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class AccountService
{
    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly PlanService _plan;

    public AccountService(IFinanceRepository repo, IUnitOfWork uow, PlanService plan)
    {
        _repo = repo;
        _uow = uow;
        _plan = plan;
    }

    public IReadOnlyList<BankOptionResponse> ListBanks()
        => BrazilianBanks.All.Select(b => new BankOptionResponse(b.Code, b.Name)).ToList();

    public async Task<IReadOnlyList<AccountResponse>> ListAsync(bool includeArchived, CancellationToken ct)
    {
        var items = await _repo.ListAsync<Account>(a => includeArchived || !a.IsArchived, ct);
        return items.OrderBy(a => a.Name).Select(ToResponse).ToList();
    }

    /// <summary>
    /// Cash at the end of <paramref name="endYm"/>: current balances minus movements on or after the next month.
    /// </summary>
    public async Task<CashBalanceResponse> GetCashBalanceAtEndOfMonthAsync(string endYm, CancellationToken ct)
    {
        var ym = Competence.Require(endYm);
        var cutoff = Competence.EndExclusive(ym);
        var accounts = await _repo.ListAsync<Account>(a => !a.IsArchived, ct);
        var current = accounts.Sum(a => a.Balance);
        var later = await _repo.ListAsync<Entry>(e => e.OccurredAt >= cutoff, ct);
        var laterDelta = later.SelectMany(e => e.BalanceEffects()).Sum(x => x.Delta);
        return new CashBalanceResponse(ym, current - laterDelta);
    }

    public async Task<AccountResponse> CreateAsync(CreateAccountRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var account = Account.Create(
            request.Name,
            request.Kind,
            request.BankCode,
            request.Agency,
            request.AccountNumber,
            request.CheckDigit);
        _repo.Add(account);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(account);
    }

    public async Task<AccountResponse> UpdateAsync(Guid id, UpdateAccountRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var account = await RequireAsync(id, ct);
        account.Update(request.Name, request.Kind, request.BankCode, request.Agency, request.AccountNumber, request.CheckDigit);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(account);
    }

    public async Task<AccountResponse> ArchiveAsync(Guid id, bool archived, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var account = await RequireAsync(id, ct);
        account.SetArchived(archived);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(account);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var account = await RequireAsync(id, ct);
        if (await _repo.AnyAsync<Entry>(e => e.AccountId == id || e.ContraAccountId == id, ct))
            throw new ValidationException("id", "Conta com movimentações não pode ser excluída. Arquive-a.");

        _repo.SoftDelete(account);
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<Account> RequireAsync(Guid id, CancellationToken ct)
        => await _repo.GetAsync<Account>(id, ct) ?? throw new NotFoundException("Conta", id);

    private static AccountResponse ToResponse(Account a)
        => new(
            a.Id,
            a.Name,
            a.Kind,
            a.Balance,
            a.IsArchived,
            a.BankCode,
            a.Agency,
            a.AccountNumber,
            a.CheckDigit,
            BrazilianBanks.Find(a.BankCode)?.Name);
}
