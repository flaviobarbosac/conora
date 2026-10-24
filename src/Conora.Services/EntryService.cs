using Conora.Domain;
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
    private readonly ITenantContext _tenant;
    private readonly ISupplierStore _suppliers;
    private readonly PlanService _plan;
    private readonly MonthService _months;
    private readonly CategoryService _categories;
    private readonly FamilyGroupService _family;

    public EntryService(
        IFinanceRepository repo,
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        ITenantContext tenant,
        ISupplierStore suppliers,
        PlanService plan,
        MonthService months,
        CategoryService categories,
        FamilyGroupService family)
    {
        _repo = repo;
        _audits = audits;
        _uow = uow;
        _correlation = correlation;
        _tenant = tenant;
        _suppliers = suppliers;
        _plan = plan;
        _months = months;
        _categories = categories;
        _family = family;
    }

    public async Task<PagedResponse<EntryResponse>> SearchAsync(EntryFilter filter, CancellationToken ct)
    {
        var ym = string.IsNullOrWhiteSpace(filter.CompetenceYm) ? null : Competence.Require(filter.CompetenceYm);
        var type = filter.Type;
        var accountId = filter.AccountId;
        var search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim().ToLowerInvariant();
        var skip = Math.Max(0, filter.Skip);
        var take = Math.Clamp(filter.Take, 1, 200);

        var scope = await _family.ResolveSharingScopeAsync(filter.Scope, ct);
        var userIds = await _family.GetReadableUsuarioIdsAsync(ct, scope);
        var selfId = _family.RequireUserId();
        var multi = userIds.Count > 1;

        var categoryIds = filter.CategoryId is Guid rootId
            ? await ResolveCategoryFilterIdsAsync(rootId, userIds, ct)
            : null;

        System.Linq.Expressions.Expression<Func<Entry, bool>> predicate = e =>
            (ym == null || e.CompetenceYm == ym)
            && (type == null || e.Type == type)
            && (categoryIds == null
                || (e.CategoryId != null && categoryIds.Contains(e.CategoryId.Value)))
            && (accountId == null || e.AccountId == accountId || e.ContraAccountId == accountId)
            && (search == null || e.Description.ToLower().Contains(search));

        List<Entry> items;
        int total;
        if (multi)
        {
            items = await _repo.QueryForUsuariosAsync<Entry, Entry>(
                userIds,
                q => q.Where(predicate)
                    .OrderByDescending(e => e.OccurredAt)
                    .ThenByDescending(e => e.CreatedAt)
                    .Skip(skip)
                    .Take(take),
                ct);
            total = (await _repo.ListForUsuariosAsync<Entry>(userIds, predicate, ct)).Count;
        }
        else
        {
            total = await _repo.CountAsync(predicate, ct);
            items = await _repo.QueryAsync<Entry, Entry>(
                q => q.Where(predicate)
                    .OrderByDescending(e => e.OccurredAt)
                    .ThenByDescending(e => e.CreatedAt)
                    .Skip(skip)
                    .Take(take),
                ct);
        }

        var map = await BuildResponseMapAsync(items, userIds, selfId, multi, ct);
        return new PagedResponse<EntryResponse>(
            items.Select(e => ToResponse(e, map)).ToList(),
            skip,
            take,
            total);
    }

    /// <summary>
    /// When filtering by a group/master account, include that id, every descendant on the
    /// viewer's chart, and peer replicas (same code/name) so Family aggregate drill-down works.
    /// Cross-tenant reads stay inside <paramref name="userIds"/> only.
    /// </summary>
    private async Task<HashSet<Guid>> ResolveCategoryFilterIdsAsync(
        Guid categoryId,
        IReadOnlyList<Guid> userIds,
        CancellationToken ct)
    {
        var accounts = userIds.Count > 1
            ? await _repo.ListForUsuariosAsync<Category>(userIds, c => !c.IsPendingReview, ct)
            : await _repo.ListAsync<Category>(c => !c.IsPendingReview, ct);

        var root = accounts.FirstOrDefault(a => a.Id == categoryId)
                   ?? throw new NotFoundException("Conta", categoryId);

        var sameChart = accounts.Where(a => a.UsuarioId == root.UsuarioId).ToList();
        var localIds = DescendantsOf(sameChart, categoryId);
        if (userIds.Count <= 1)
            return localIds;

        var keys = sameChart
            .Where(a => localIds.Contains(a.Id))
            .Select(a => (a.Code, a.Name))
            .ToList();
        var ids = new HashSet<Guid>(localIds);
        foreach (var peer in accounts)
        {
            if (ids.Contains(peer.Id))
                continue;
            if (keys.Any(k =>
                    (k.Code is not null && peer.Code == k.Code)
                    || string.Equals(peer.Name, k.Name, StringComparison.OrdinalIgnoreCase)))
                ids.Add(peer.Id);
        }

        return ids;
    }

    private static HashSet<Guid> DescendantsOf(IReadOnlyList<Category> accounts, Guid rootId)
    {
        var byParent = accounts
            .Where(a => a.ParentId != null)
            .GroupBy(a => a.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Id).ToList());

        var ids = new HashSet<Guid> { rootId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!byParent.TryGetValue(current, out var children))
                continue;
            foreach (var childId in children)
            {
                if (ids.Add(childId))
                    queue.Enqueue(childId);
            }
        }

        return ids;
    }

    public async Task<EntryResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var selfId = _family.RequireUserId();
        var userIds = await _family.GetReadableUsuarioIdsAsync(ct);
        var entry = userIds.Count > 1
            ? await _repo.FirstOrDefaultForUsuariosAsync<Entry>(userIds, e => e.Id == id, ct)
            : await _repo.FirstOrDefaultAsync<Entry>(e => e.Id == id, ct, track: false);
        if (entry is null)
            throw new NotFoundException("Lançamento", id);

        var map = await BuildResponseMapAsync([entry], userIds, selfId, userIds.Count > 1, ct);
        return ToResponse(entry, map);
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
            await ApplyPatrimonyAsync(entry, 1, ct);
        }

        AuditRecorder.Record(_audits, _correlation, "Entry", created[0].Id, "EntryCreated",
            new { request.Type, request.Amount, Count = count, Competence = competences[0] });
        await _uow.SaveChangesAsync(ct);
        return created.Select(e => ToResponse(e)).ToList();
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

        var previousCategoryId = entry.CategoryId;
        await EntryLedger.ApplyAsync(_repo, entry, -1, ct);
        await ApplyProjectAsync(entry, -1, ct);
        await ApplyPatrimonyAsync(entry, -1, ct);
        entry.Update(request.Amount, newDate, newYm, request.AccountId, request.ContraAccountId,
            categoryId, request.IncomeSourceId, request.MemberId, request.Description);
        await EntryLedger.ApplyAsync(_repo, entry, 1, ct);
        await ApplyProjectAsync(entry, 1, ct);
        await ApplyPatrimonyAsync(entry, 1, ct);

        if (entry.Type == EntryType.Expense
            && categoryId is Guid newCategoryId
            && newCategoryId != previousCategoryId)
        {
            await TryRecordSupplierOverrideAsync(entry.Description, newCategoryId, ct);
        }

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
        await ApplyPatrimonyAsync(entry, -1, ct);
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

    /// <summary>Suggests the Category most used for entries with a similar description.</summary>
    public async Task<CategorySuggestionResponse> SuggestAccountAsync(string description, CancellationToken ct)
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

        var account = await _repo.FirstOrDefaultAsync<Category>(c => c.Id == categoryId, ct, track: false);
        return new CategorySuggestionResponse(account?.Id, account?.Name);
    }

    private static void EnsureEditable(Entry entry)
    {
        if (entry.Type == EntryType.CardPayment)
            throw new ValidationException("type", "Pagamento de fatura não pode ser editado ou excluído por aqui.");
    }

    /// <summary>
    /// When the person changes an expense category, persist a per-user override for that supplier
    /// (matched by CNPJ in the description or by normalized razão social).
    /// </summary>
    private async Task TryRecordSupplierOverrideAsync(string description, Guid categoryId, CancellationToken ct)
    {
        var userId = _tenant.UsuarioId;
        if (userId is null)
            return;

        var category = await _repo.FirstOrDefaultAsync<Category>(c => c.Id == categoryId, ct, track: false);
        if (category?.Code is null)
            return;

        Supplier? supplier = null;
        if (BrazilianCnpj.TryExtract(description, out var cnpj))
            supplier = await _suppliers.FindByCnpjAsync(cnpj, ct);

        if (supplier is null && !string.IsNullOrWhiteSpace(description))
            supplier = await _suppliers.FindByNormalizedNameAsync(Supplier.NormalizeName(description), ct);

        if (supplier is null)
            return;

        await _suppliers.UpsertUserCategoryAsync(supplier.Id, userId.Value, category.Code, ct);
    }

    private async Task<Guid?> ResolveCategoryAsync(EntryType type, Guid? requested, CancellationToken ct)
    {
        if (type == EntryType.Contribution)
            return (await _categories.GetByCodeAsync(SystemCategories.Tithe, ct)).Id;

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
            var category = await _categories.RequireAnalyticalAsync(cid, ct);
            var ok = type switch
            {
                EntryType.Income => category.Section is CategorySection.Income
                    or CategorySection.LifeProject
                    or CategorySection.Asset
                    or CategorySection.Liability,
                EntryType.Expense => category.Section is CategorySection.Discount
                    or CategorySection.Essential
                    or CategorySection.Social
                    or CategorySection.Debt
                    or CategorySection.Asset
                    or CategorySection.Liability,
                EntryType.Contribution => category.Section is CategorySection.Discount
                    or CategorySection.Essential
                    or CategorySection.Social,
                EntryType.ProjectContribution => category.Section == CategorySection.LifeProject,
                _ => true
            };
            if ((type is EntryType.Income or EntryType.Expense or EntryType.Contribution
                    or EntryType.ProjectContribution)
                && !ok)
                throw new ValidationException("categoryId", "A conta não é compatível com o tipo do lançamento.");
        }

        if (memberId is Guid mid && !await _repo.AnyAsync<FamilyMember>(m => m.Id == mid, ct))
            throw new NotFoundException("Membro", mid);

        if (lifeProjectId is Guid pid)
            await RequireVisibleProjectAsync(pid, ct);
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
            throw new ValidationException("incomeSourceId", "Renda vinculada a uma fonte não aceita parcelas ou recorrência.");
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
        if (entry.Type == EntryType.ProjectContribution && entry.LifeProjectId is Guid pid)
        {
            var linked = await RequireVisibleProjectAsync(pid, ct, track: true);
            linked.ApplyContribution(entry.Amount * sign);
            return;
        }

        if (entry.Type != EntryType.Income || entry.CategoryId is not Guid categoryId)
            return;

        var category = await _repo.FirstOrDefaultAsync<Category>(c => c.Id == categoryId, ct, track: false);
        if (category?.Section != CategorySection.LifeProject)
            return;

        var peers = await _family.GetReadableUsuarioIdsAsync(ct);
        var matches = await _repo.ListForUsuariosAsync<LifeProject>(peers, p => p.CategoryId == categoryId, ct, track: true);
        var project = entry.LifeProjectId is Guid linkedId
            ? matches.FirstOrDefault(p => p.Id == linkedId)
            : matches.Count == 1 ? matches[0] : null;
        project?.ApplyContribution(entry.Amount * sign);
    }

    /// <summary>Income raises the patrimony item; expense lowers it. One item in the category, or the one named in the description.</summary>
    private async Task ApplyPatrimonyAsync(Entry entry, int sign, CancellationToken ct)
    {
        if (entry.Type is not (EntryType.Income or EntryType.Expense) || entry.CategoryId is not Guid categoryId)
            return;

        var category = await _repo.FirstOrDefaultAsync<Category>(c => c.Id == categoryId, ct, track: false);
        if (category is null || category.Section is not (CategorySection.Asset or CategorySection.Liability))
            return;

        var items = await _repo.ListAsync<PatrimonyItem>(p => p.CategoryId == categoryId, ct, track: true);
        var item = items.Count == 1
            ? items[0]
            : items.FirstOrDefault(p => string.Equals(p.Name, entry.Description, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return;

        var direction = entry.Type == EntryType.Expense ? -1 : 1;
        item.SetAmount(item.Amount + entry.Amount * sign * direction);
    }

    private async Task<LifeProject> RequireVisibleProjectAsync(Guid projectId, CancellationToken ct, bool track = false)
    {
        var own = await _repo.FirstOrDefaultAsync<LifeProject>(p => p.Id == projectId, ct, track);
        if (own is not null)
            return own;

        var peers = await _family.GetReadableUsuarioIdsAsync(ct);
        var matches = await _repo.ListForUsuariosAsync<LifeProject>(peers, p => p.Id == projectId, ct, track);
        var project = matches.FirstOrDefault() ?? throw new NotFoundException("Projeto de vida", projectId);
        if (project.Scope != LifeProjectScope.Group || !await _family.IsInSameGroupAsync(project.UsuarioId, ct))
            throw new NotFoundException("Projeto de vida", projectId);
        return project;
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

    private async Task<EntryResponseMap> BuildResponseMapAsync(
        IReadOnlyList<Entry> items,
        IReadOnlyList<Guid> userIds,
        Guid selfId,
        bool multi,
        CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        if (multi && items.Count > 0)
        {
            var group = await _family.GetGroupAsync(ct);
            foreach (var member in group.Members)
                names[member.UsuarioId] = FirstName(member.Name);
        }

        Dictionary<Guid, Guid>? categoryRemap = null;
        if (multi)
        {
            var selfCategories = await _repo.ListAsync<Category>(c => !c.IsPendingReview, ct);
            var peerIds = items
                .Select(e => e.CategoryId)
                .Where(id => id is Guid cid && selfCategories.All(c => c.Id != cid))
                .Select(id => id!.Value)
                .Distinct()
                .ToList();
            if (peerIds.Count > 0)
            {
                var peers = await _repo.ListForUsuariosAsync<Category>(
                    userIds, c => peerIds.Contains(c.Id) && !c.IsPendingReview, ct);
                categoryRemap = new Dictionary<Guid, Guid>();
                foreach (var peer in peers)
                {
                    var local = selfCategories.FirstOrDefault(c =>
                        (peer.Code is not null && c.Code == peer.Code)
                        || string.Equals(c.Name, peer.Name, StringComparison.OrdinalIgnoreCase));
                    if (local is not null)
                        categoryRemap[peer.Id] = local.Id;
                }
            }
        }

        return new EntryResponseMap(selfId, names, categoryRemap);
    }

    private static string FirstName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return "Membro";
        var part = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return part;
    }

    private static EntryResponse ToResponse(Entry e, EntryResponseMap? map = null)
    {
        var selfId = map?.SelfId ?? e.UsuarioId;
        var canEdit = e.UsuarioId == selfId;
        var categoryId = e.CategoryId;
        if (categoryId is Guid cid
            && map?.CategoryRemap is not null
            && map.CategoryRemap.TryGetValue(cid, out var localId))
            categoryId = localId;

        string? postedBy = null;
        if (!canEdit && map is not null)
            postedBy = map.Names.GetValueOrDefault(e.UsuarioId);

        return new(
            e.Id, e.Type, e.Amount, e.OccurredAt, e.CompetenceYm, e.AccountId, e.ContraAccountId, categoryId,
            e.IncomeSourceId, e.CreditCardId, e.LifeProjectId, e.MemberId, e.Description, e.RecurrenceKey,
            e.InstallmentNumber, e.InstallmentCount, e.UsuarioId, postedBy, canEdit);
    }

    private sealed record EntryResponseMap(
        Guid SelfId,
        IReadOnlyDictionary<Guid, string> Names,
        IReadOnlyDictionary<Guid, Guid>? CategoryRemap);
}
