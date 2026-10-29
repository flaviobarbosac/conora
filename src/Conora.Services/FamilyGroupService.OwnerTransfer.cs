using Conora.Domain.Entities;
using Conora.Domain.Services;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Conora.Services;

public sealed partial class FamilyGroupService
{
    public async Task<FamilyOwnerTransferResponse> RequestOwnerTransferAsync(
        FamilyOwnerTransferRequest request,
        CancellationToken ct)
    {
        var self = RequireUserId();
        var (group, isOwner) = await RequireGroupAsync(ct);
        if (!isOwner)
            throw new ForbiddenException("Só o responsável pelo orçamento pode transferir.");
        if (!FamilySuccessionRules.CanRequest(DateTime.UtcNow))
            throw new ValidationException("transfer", FamilySuccessionRules.OutsideWindowMessage);

        if (await _family.FindOpenOwnerTransferAsync(group.Id, ct) is not null)
            throw new ValidationException("transfer", "Já existe uma transferência aberta.");

        var members = await _family.ListActiveMembersAsync(group.Id, ct);
        if (members.All(m => m.UsuarioId != request.ToUsuarioId))
            throw new ValidationException("toUsuarioId", "Escolha um membro do grupo.");

        var transfer = FamilyOwnerTransfer.Create(group.Id, self, request.ToUsuarioId, OwnerTransferTtl);
        _family.AddOwnerTransfer(transfer);

        var from = await RequireUserAsync(ct);
        var to = await _family.FindUserByIdAsync(request.ToUsuarioId, ct)
                 ?? throw new NotFoundException("Usuário", request.ToUsuarioId);

        var link = $"{PublicWebBase()}/app/grupo/owner/{transfer.Token}";
        try
        {
            await _email.SendAsync(
                to.Email,
                $"{from.Name} quer transferir o orçamento familiar no Conora",
                new EmailContent(
                    "Transferência do orçamento familiar",
                    [
                        $"{from.Name} pediu que você assuma o orçamento do grupo familiar no Conora.",
                        "Ao autorizar no QR, você aceita assumir a administração. O orçamento passa para você quando o titular cancelar a assinatura na loja. Você tem 24 horas depois disso para assinar e o grupo seguir com você."
                    ])
                {
                    Preheader = "Pedido de transferência do orçamento familiar.",
                    ActionLabel = "Autorizar",
                    ActionUrl = link,
                    Note = $"O pedido vale até {transfer.ExpiresAt:dd/MM/yyyy HH:mm} UTC."
                },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Owner-transfer e-mail failed for {Email}; transfer kept.", to.Email);
        }

        _family.AddNotice(FamilyNotice.Create(
            to.Id,
            "OwnerTransferRequested",
            $"{from.Name} pediu que você assuma o orçamento familiar."));

        AuditRecorder.Record(_audits, _correlation, "FamilyOwnerTransfer", transfer.Id, "OwnerTransferRequested",
            new { transfer.ToUsuarioId });
        await _uow.SaveChangesAsync(ct);

        return new FamilyOwnerTransferResponse(
            transfer.Id, self, from.Name, to.Id, to.Name, transfer.ExpiresAt, true, false, false, link);
    }

    public async Task CancelOwnerTransferAsync(CancellationToken ct)
    {
        var (group, isOwner) = await RequireGroupAsync(ct);
        if (!isOwner)
            throw new ForbiddenException("Só o responsável pode cancelar a transferência.");
        var pending = await _family.FindOpenOwnerTransferAsync(group.Id, ct, track: true)
                      ?? throw new NotFoundException("Transferência", group.Id);
        pending.Cancel();
        AuditRecorder.Record(_audits, _correlation, "FamilyOwnerTransfer", pending.Id, "OwnerTransferCancelled", new { });
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<FamilyGroupResponse> AcceptOwnerTransferAsync(string token, CancellationToken ct)
    {
        await _uow.ExecuteInTransactionAsync(innerCt => CompleteOwnerTransferAsync(token, innerCt), ct);
        return await GetGroupAsync(ct);
    }

    private async Task CompleteOwnerTransferAsync(string token, CancellationToken ct)
    {
        var self = RequireUserId();
        var transfer = await _family.FindOwnerTransferByTokenAsync(token, ct, track: true)
                       ?? throw new ValidationException("token", "Transferência inválida.");
        var wasOpen = transfer.AuthorizedAt is null && transfer.IsOpen;
        transfer.Authorize(self);

        var group = await _family.GetGroupAsync(transfer.FamilyGroupId, ct)
                    ?? throw new ValidationException("token", "Grupo não encontrado.");
        var members = await _family.ListActiveMembersAsync(group.Id, ct);
        if (members.All(m => m.UsuarioId != self))
            throw new ValidationException("token", "Você não faz mais parte deste grupo.");

        if (wasOpen)
        {
            _family.AddNotice(FamilyNotice.Create(
                transfer.FromUsuarioId,
                "OwnerTransferAccepted",
                "A autorização para assumir a administração foi registrada."));

            AuditRecorder.Record(_audits, _correlation, "FamilyOwnerTransfer", transfer.Id, "OwnerTransferAccepted",
                new { transfer.FromUsuarioId, to = self });
        }

        await _uow.SaveChangesAsync(ct);
    }

    public async Task ConfirmStoreCancelledAsync(CancellationToken ct)
    {
        var self = RequireUserId();
        if (await _family.FindAuthorizedHandoverAsync(self, ct) is null)
            throw new ValidationException("transfer", "A outra pessoa ainda não autorizou a transferência.");
        await MoveBudgetsOnStoreCancellationAsync(self, ct);
        await _storeBilling.CancelOwnerSubscriptionAsync(self, ct);
    }

    public async Task MoveBudgetsOnStoreCancellationAsync(Guid ownerUsuarioId, CancellationToken ct)
    {
        var transfer = await _family.FindAuthorizedHandoverAsync(ownerUsuarioId, ct, track: true);
        if (transfer is null || transfer.BudgetsMovedAt is not null)
            return;

        var handover = FamilySuccessionRules.HandoverMonth(DateTime.UtcNow);
        var previousTenant = _tenant.UsuarioId;
        _tenant.Set(transfer.ToUsuarioId);
        try
        {
            await ReplaceBudgetsFromHandoverAsync(transfer.FromUsuarioId, transfer.ToUsuarioId, handover, ct);
            transfer.MarkBudgetsMoved(DateTime.UtcNow);
            await _uow.SaveChangesAsync(ct);
        }
        finally
        {
            if (previousTenant is Guid tenantId)
                _tenant.Set(tenantId);
        }
    }

    private async Task ReplaceBudgetsFromHandoverAsync(
        Guid fromUsuarioId,
        Guid toUsuarioId,
        string handoverMonth,
        CancellationToken ct)
    {
        var toBudgets = await _repo.ListForUsuariosAsync<Budget>([toUsuarioId], _ => true, ct, track: true);
        foreach (var toBudget in toBudgets.Where(budget => FamilySuccessionRules.IsHandoverOrLater(budget.CompetenceYm, handoverMonth)))
        {
            var existingLines = await _repo.ListForUsuariosAsync<BudgetLine>(
                [toUsuarioId], line => line.BudgetId == toBudget.Id, ct, track: true);
            foreach (var line in existingLines)
                _repo.SoftDelete(line);
            _repo.SoftDelete(toBudget);
        }

        var fromBudgets = (await _repo.ListForUsuariosAsync<Budget>([fromUsuarioId], _ => true, ct))
            .Where(budget => FamilySuccessionRules.IsHandoverOrLater(budget.CompetenceYm, handoverMonth))
            .ToList();
        if (fromBudgets.Count == 0)
            return;

        var toCategories = (await _repo.ListForUsuariosAsync<Category>(
                [toUsuarioId], c => c.Level == CategoryLevel.Analytical, ct))
            .ToList();
        var toByCode = toCategories
            .Where(c => !string.IsNullOrWhiteSpace(c.Code))
            .GroupBy(c => c.Code!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var toByFamilyKey = toCategories
            .Where(c => c.FamilyKey is not null)
            .GroupBy(c => c.FamilyKey!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var toByName = toCategories
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var stagedLines = new Dictionary<(Guid BudgetId, Guid CategoryId), BudgetLine>();

        foreach (var fromBudget in fromBudgets)
        {
            var lines = await _repo.ListForUsuariosAsync<BudgetLine>(
                [fromUsuarioId], l => l.BudgetId == fromBudget.Id, ct);
            if (lines.Count == 0)
                continue;

            var toBudget = await _repo.FirstOrDefaultAsync<Budget>(
                b => b.CompetenceYm == fromBudget.CompetenceYm, ct, track: true);
            if (toBudget is null)
            {
                toBudget = Budget.Create(fromBudget.CompetenceYm, fromBudget.Mode);
                _repo.Add(toBudget);
            }
            else
            {
                toBudget.SetMode(fromBudget.Mode);
            }

            var fromCategories = await _repo.ListForUsuariosAsync<Category>(
                [fromUsuarioId], c => lines.Select(l => l.CategoryId).Contains(c.Id), ct);

            foreach (var line in lines)
            {
                var fromCat = fromCategories.FirstOrDefault(c => c.Id == line.CategoryId);
                if (fromCat is null)
                    continue;

                Category? toCat = null;
                if (!string.IsNullOrWhiteSpace(fromCat.Code))
                    toByCode.TryGetValue(fromCat.Code, out toCat);
                if (toCat is null && fromCat.FamilyKey is Guid familyKey)
                    toByFamilyKey.TryGetValue(familyKey, out toCat);
                toCat ??= toByName.GetValueOrDefault(fromCat.Name);
                if (toCat is null)
                    continue;

                var key = (toBudget.Id, toCat.Id);
                if (stagedLines.TryGetValue(key, out var staged))
                {
                    staged.SetPlanned(line.PlannedAmount);
                    staged.SetExpenseDay(line.ExpenseDay);
                    continue;
                }

                var existing = await _repo.FirstOrDefaultAsync<BudgetLine>(
                    l => l.BudgetId == toBudget.Id && l.CategoryId == toCat.Id, ct, track: true);
                if (existing is null)
                {
                    var created = BudgetLine.Create(toBudget.Id, toCat.Id, line.PlannedAmount, line.ExpenseDay);
                    _repo.Add(created);
                    stagedLines[key] = created;
                }
                else
                {
                    existing.SetPlanned(line.PlannedAmount);
                    existing.SetExpenseDay(line.ExpenseDay);
                    stagedLines[key] = existing;
                }
            }
        }
    }
}
