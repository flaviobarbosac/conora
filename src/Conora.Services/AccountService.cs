using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class AccountService
{
    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly PlanService _plan;
    private readonly EntryService _entries;

    public AccountService(IFinanceRepository repo, IUnitOfWork uow, PlanService plan, EntryService entries)
    {
        _repo = repo;
        _uow = uow;
        _plan = plan;
        _entries = entries;
    }

    public IReadOnlyList<BankOptionResponse> ListBanks()
        => BrazilianBanks.All.Select(b => new BankOptionResponse(b.Code, b.Name)).ToList();

    public async Task<IReadOnlyList<AccountResponse>> ListAsync(bool includeArchived, CancellationToken ct)
    {
        var items = await _repo.ListAsync<Account>(a => includeArchived || !a.IsArchived, ct);
        return items.OrderBy(a => a.Name).Select(ToResponse).ToList();
    }

    public async Task<AccountResponse> CreateAsync(CreateAccountRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var account = Account.Create(
            request.Name,
            request.Kind,
            request.OpeningBalance,
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

    /// <summary>A transfer is an entry of type Transfer: it never changes the monthly result.</summary>
    public async Task<EntryResponse> TransferAsync(TransferRequest request, CancellationToken ct)
    {
        var created = await _entries.CreateAsync(new CreateEntryRequest(
            EntryType.Transfer,
            request.Amount,
            request.OccurredAt,
            string.IsNullOrWhiteSpace(request.Description) ? "Transferência entre contas" : request.Description,
            AccountId: request.FromAccountId,
            ContraAccountId: request.ToAccountId), ct);

        return created[0];
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
