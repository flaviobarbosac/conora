using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class EntryService
{
    private const int MaxRepeat = 60;

    private readonly IFinanceRepository _repo;
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ICorrelationContext _correlation;
    private readonly PlanService _plan;
    private readonly MonthService _months;
    private readonly CategoryService _categories;

    public EntryService(
        IFinanceRepository repo,
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        PlanService plan,
        MonthService months,
        CategoryService categories)
    {
        _repo = repo;
        _audits = audits;
        _uow = uow;
        _correlation = correlation;
        _plan = plan;
        _months = months;
        _categories = categories;
    }

    public async Task<PagedResponse<EntryResponse>> SearchAsync(EntryFilter filter, CancellationToken ct)
    {
        var ym = string.IsNullOrWhiteSpace(filter.CompetenceYm) ? null : Competence.Require(filter.CompetenceYm);
        var type = filter.Type;
        var categoryId = filter.CategoryId;
        var accountId = filter.AccountId;
        var search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim().ToLowerInvariant();
        var skip = Math.Max(0, filter.Skip);
        var take = Math.Clamp(filter.Take, 1, 200);

        System.Linq.Expressions.Expression<Func<Entry, bool>> predicate = e =>
            (ym == null || e.CompetenceYm == ym)
            && (type == null || e.Type == type)
            && (categoryId == null || e.CategoryId == categoryId)
            && (accountId == null || e.AccountId == accountId || e.ContraAccountId == accountId)
            && (search == null || e.Description.ToLower().Contains(search));

        var total = await _repo.CountAsync(predicate, ct);
        var items = await _repo.QueryAsync<Entry, Entry>(
            q => q.Where(predicate).OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.CreatedAt).Skip(skip).Take(take), ct);

        return new PagedResponse<EntryResponse>(items.Select(ToResponse).ToList(), skip, take, total);
    }

    public async Task<EntryResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var entry = await _repo.FirstOrDefaultAsync<Entry>(e => e.Id == id, ct, track: false)
                    ?? throw new NotFoundException("Lançamento", id);
        return ToResponse(entry);
    }

    public async Task<IReadOnlyList<EntryResponse>> CreateAsync(CreateEntryRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);

        if (request.Type == EntryType.CardPayment)
            throw new ValidationException("type", "Pagamento de fatura é feito pela fatura do cartão.");

        var installments = request.InstallmentCount ?? 1;
        var repeat = request.RepeatMonths ?? 1;
        if (installments < 1 || installments > MaxRepeat)
            throw new ValidationException("installmentCount", $"Parcelas devem estar entre 1 e {MaxRepeat}.");
        if (repeat < 1 || repeat > MaxRepeat)
            throw new ValidationException("repeatMonths", $"Recorrência deve estar entre 1 e {MaxRepeat} meses.");
        if (installments > 1 && repeat > 1)
            throw new ValidationException("repeatMonths", "Use parcelas ou recorrência, não ambos.");
        if (request.Type is EntryType.Transfer or EntryType.ProjectContribution && (installments > 1 || repeat > 1))
            throw new ValidationException("installmentCount", "Este tipo de lançamento não aceita parcelas ou recorrência.");
        if (request.Amount <= 0)
            throw new ValidationException("amount", "Valor deve ser maior que zero.");

        var count = Math.Max(installments, repeat);
        var categoryId = await ResolveCategoryAsync(request.Type, request.CategoryId, ct);
        var firstDate = Competence.ToUtc(request.OccurredAt);
        var baseYm = string.IsNullOrWhiteSpace(request.CompetenceYm) ? Competence.From(firstDate) : Competence.Require(request.CompetenceYm);
        var competences = Enumerable.Range(0, count).Select(i => Competence.AddMonths(baseYm, i)).ToList();

        await _months.EnsureOpenAsync(competences, ct);
        await ValidateReferencesAsync(
            request.Type, request.AccountId, request.ContraAccountId, categoryId, request.MemberId, request.LifeProjectId, ct);
        await ValidateIncomeLinkAsync(request.Type, request.IncomeSourceId, request.Amount, competences[0], count, null, ct);
        await ValidateContributionDuplicateAsync(request.Type, request.Amount, competences[0], request.ConfirmDuplicate, null, ct);

        var amounts = SplitAmounts(request.Amount, installments, count);
        var groupKey = count > 1 ? Guid.NewGuid().ToString("N") : null;
        var created = new List<Entry>(count);
        for (var i = 0; i < count; i++)
        {
            created.Add(Entry.Create(
                request.Type,
                amounts[i],
                firstDate.AddMonths(i),
                request.Description,
                competences[i],
                request.AccountId,
                request.ContraAccountId,
                categoryId,
                request.IncomeSourceId,
                lifeProjectId: request.LifeProjectId,
                memberId: request.MemberId,
                recurrenceKey: groupKey,
                installmentNumber: installments > 1 ? i + 1 : null,
                installmentCount: installments > 1 ? installments : null));
        }

        _repo.AddRange(created);
        foreach (var entry in created)
        {
            await EntryLedger.ApplyAsync(_repo, entry, 1, ct);
            await ApplyProjectAsync(entry, 1, ct);
        }

        AuditRecorder.Record(_audits, _correlation, "Entry", created[0].Id, "EntryCreated",
            new { request.Type, request.Amount, Count = count, Competence = competences[0] });
        await _uow.SaveChangesAsync(ct);
        return created.Select(ToResponse).ToList();
    }

    public async Task<EntryResponse> UpdateAsync(Guid id, UpdateEntryRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var entry = await _repo.GetAsync<Entry>(id, ct) ?? throw new NotFoundException("Lançamento", id);
        EnsureEditable(entry);

        var oldYm = entry.CompetenceYm;
        var newDate = Competence.ToUtc(request.OccurredAt);
        var newYm = string.IsNullOrWhiteSpace(request.CompetenceYm) ? Competence.From(newDate) : Competence.Require(request.CompetenceYm);
        await _months.EnsureOpenAsync([oldYm, newYm], ct);

        var categoryId = await ResolveCategoryAsync(entry.Type, request.CategoryId, ct);
        await ValidateReferencesAsync(
            entry.Type, request.AccountId, request.ContraAccountId, categoryId, request.MemberId, entry.LifeProjectId, ct);
        await ValidateIncomeLinkAsync(entry.Type, request.IncomeSourceId, request.Amount, newYm, 1, entry.Id, ct);
        await ValidateContributionDuplicateAsync(entry.Type, request.Amount, newYm, request.ConfirmDuplicate, entry.Id, ct);

        await EntryLedger.ApplyAsync(_repo, entry, -1, ct);
        await ApplyProjectAsync(entry, -1, ct);
        entry.Update(request.Amount, newDate, newYm, request.AccountId, request.ContraAccountId,
            categoryId, request.IncomeSourceId, request.MemberId, request.Description);
        await EntryLedger.ApplyAsync(_repo, entry, 1, ct);
        await ApplyProjectAsync(entry, 1, ct);

        AuditRecorder.Record(_audits, _correlation, "Entry", entry.Id, "EntryUpdated", new { entry.Type, entry.Amount, entry.CompetenceYm });
        await _uow.SaveChangesAsync(ct);
        return ToResponse(entry);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var entry = await _repo.GetAsync<Entry>(id, ct) ?? throw new NotFoundException("Lançamento", id);
        EnsureEditable(entry);
        await _months.EnsureOpenAsync(entry.CompetenceYm, ct);

        await EntryLedger.ApplyAsync(_repo, entry, -1, ct);
        await ApplyProjectAsync(entry, -1, ct);
        _repo.SoftDelete(entry);

        AuditRecorder.Record(_audits, _correlation, "Entry", entry.Id, "EntryDeleted", new { entry.Type, entry.Amount, entry.CompetenceYm });
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EntryResponse>> DuplicateAsync(Guid id, DateTime? occurredAt, CancellationToken ct)
    {
        var source = await _repo.FirstOrDefaultAsync<Entry>(e => e.Id == id, ct, track: false)
                     ?? throw new NotFoundException("Lançamento", id);

        // A duplicate never copies the income-source link (one source, one income per competence).
        return await CreateAsync(new CreateEntryRequest(
            source.Type,
            source.Amount,
            occurredAt ?? DateTime.UtcNow,
            source.Description,
            AccountId: source.AccountId,
            ContraAccountId: source.ContraAccountId,
            CategoryId: source.CategoryId,
            LifeProjectId: source.LifeProjectId,
            MemberId: source.MemberId,
            ConfirmDuplicate: true), ct);
    }

    /// <summary>Suggests the category most used for entries with a similar description.</summary>
    public async Task<CategorySuggestionResponse> SuggestCategoryAsync(string description, CancellationToken ct)
    {
        var token = (description ?? string.Empty)
            .ToLowerInvariant()
            .Split([' ', '-', '/', ','], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(w => w.Length >= 3);
        if (token is null)
            return new CategorySuggestionResponse(null, null);

        var ranked = await _repo.QueryAsync<Entry, Guid?>(q => q
            .Where(e => e.CategoryId != null && e.Description.ToLower().Contains(token))
            .GroupBy(e => e.CategoryId)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(1), ct);

        if (ranked.Count == 0 || ranked[0] is not Guid categoryId)
            return new CategorySuggestionResponse(null, null);

        var category = await _repo.FirstOrDefaultAsync<Category>(c => c.Id == categoryId, ct, track: false);
        return new CategorySuggestionResponse(category?.Id, category?.Name);
    }

    private static void EnsureEditable(Entry entry)
    {
        if (entry.Type == EntryType.CardPayment)
            throw new ValidationException("type", "Pagamento de fatura não pode ser editado ou excluído por aqui.");
    }

    private async Task<Guid?> ResolveCategoryAsync(EntryType type, Guid? requested, CancellationToken ct)
    {
        // Contribution entries (tithe/offering) always live in Contribuições/Doações, never as income discount.
        if (type == EntryType.Contribution)
            return (await _categories.GetByCodeAsync(SystemCategories.Contributions, ct)).Id;

        return requested;
    }

    private async Task ValidateReferencesAsync(
        EntryType type,
        Guid? accountId,
        Guid? contraAccountId,
        Guid? categoryId,
        Guid? memberId,
        Guid? lifeProjectId,
        CancellationToken ct)
    {
        foreach (var id in new[] { accountId, contraAccountId }.Where(i => i.HasValue).Select(i => i!.Value))
        {
            var account = await _repo.FirstOrDefaultAsync<Account>(a => a.Id == id, ct, track: false)
                          ?? throw new NotFoundException("Conta", id);
            if (account.IsArchived)
                throw new ValidationException("accountId", $"A conta '{account.Name}' está arquivada.");
        }

        if (categoryId is Guid cid)
        {
            var category = await _repo.FirstOrDefaultAsync<Category>(c => c.Id == cid, ct, track: false)
                           ?? throw new NotFoundException("Categoria", cid);
            if (!category.IsActive)
                throw new ValidationException("categoryId", "Categoria inativa.");

            var expected = type == EntryType.Income ? CategoryKind.Income : CategoryKind.Expense;
            if (type is EntryType.Income or EntryType.Expense or EntryType.Contribution && category.Kind != expected)
                throw new ValidationException("categoryId", "A categoria não é compatível com o tipo do lançamento.");
        }

        if (memberId is Guid mid && !await _repo.AnyAsync<FamilyMember>(m => m.Id == mid, ct))
            throw new NotFoundException("Membro", mid);

        if (lifeProjectId is Guid pid && !await _repo.AnyAsync<LifeProject>(p => p.Id == pid, ct))
            throw new NotFoundException("Projeto de vida", pid);
    }

    /// <summary>One income source, one competence, one income entry (spec v1.1 §3 "Trava").</summary>
    private async Task ValidateIncomeLinkAsync(
        EntryType type, Guid? sourceId, decimal amount, string competenceYm, int count, Guid? ignoreEntryId, CancellationToken ct)
    {
        if (type != EntryType.Income || sourceId is not Guid sid)
            return;

        var source = await _repo.FirstOrDefaultAsync<IncomeSource>(s => s.Id == sid, ct, track: false)
                     ?? throw new NotFoundException("Fonte de renda", sid);

        if (count > 1)
            throw new ValidationException("incomeSourceId", "Receita vinculada a uma fonte não aceita parcelas ou recorrência.");
        if (source.CompetenceYm != competenceYm)
            throw new ValidationException("incomeSourceId", $"A fonte '{source.Name}' tem diagnóstico em {source.CompetenceYm}.");
        if (amount != source.NetSpendable)
            throw new ValidationException("amount",
                $"O valor difere da renda gastável do diagnóstico ({source.NetSpendable:0.00}). Ajuste o diagnóstico ou lance a diferença como outra receita, sem fonte.");

        var already = await _repo.AnyAsync<Entry>(
            e => e.Type == EntryType.Income && e.IncomeSourceId == sid && e.CompetenceYm == competenceYm && e.Id != ignoreEntryId, ct);
        if (already)
            throw new ValidationException("incomeSourceId", "Esta fonte já tem a receita lançada nesta competência.");
    }

    private async Task ValidateContributionDuplicateAsync(
        EntryType type, decimal amount, string competenceYm, bool confirm, Guid? ignoreEntryId, CancellationToken ct)
    {
        if (type != EntryType.Contribution || confirm)
            return;

        var rounded = decimal.Round(amount, 2);
        var exists = await _repo.AnyAsync<Entry>(
            e => e.Type == EntryType.Contribution && e.CompetenceYm == competenceYm && e.Amount == rounded && e.Id != ignoreEntryId, ct);
        if (exists)
            throw new ValidationException("confirmDuplicate", "Já existe uma contribuição de mesmo valor nesta competência. Confirme para lançar outra.");
    }

    private async Task ApplyProjectAsync(Entry entry, int sign, CancellationToken ct)
    {
        if (entry.Type != EntryType.ProjectContribution || entry.LifeProjectId is not Guid pid)
            return;

        var project = await _repo.GetAsync<LifeProject>(pid, ct) ?? throw new NotFoundException("Projeto de vida", pid);
        project.ApplyContribution(entry.Amount * sign);
    }

    private static decimal[] SplitAmounts(decimal total, int installments, int count)
    {
        var amounts = new decimal[count];
        if (installments <= 1)
        {
            Array.Fill(amounts, decimal.Round(total, 2));
            return amounts;
        }

        var each = decimal.Round(total / installments, 2);
        var allocated = 0m;
        for (var i = 0; i < installments; i++)
        {
            amounts[i] = i == installments - 1 ? decimal.Round(total, 2) - allocated : each;
            allocated += amounts[i];
        }

        return amounts;
    }

    internal static EntryResponse ToResponse(Entry e) => new(
        e.Id, e.Type, e.Amount, e.OccurredAt, e.CompetenceYm, e.AccountId, e.ContraAccountId, e.CategoryId,
        e.IncomeSourceId, e.CreditCardId, e.LifeProjectId, e.MemberId, e.Description, e.RecurrenceKey,
        e.InstallmentNumber, e.InstallmentCount);
}
