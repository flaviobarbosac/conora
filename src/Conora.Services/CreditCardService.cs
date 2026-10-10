using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class CreditCardService
{
    private readonly IFinanceRepository _repo;
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ICorrelationContext _correlation;
    private readonly PlanService _plan;
    private readonly MonthService _months;

    public CreditCardService(
        IFinanceRepository repo,
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        PlanService plan,
        MonthService months)
    {
        _repo = repo;
        _audits = audits;
        _uow = uow;
        _correlation = correlation;
        _plan = plan;
        _months = months;
    }

    public async Task<IReadOnlyList<CardResponse>> ListAsync(CancellationToken ct)
    {
        var cards = await _repo.ListAsync<CreditCard>(null, ct);
        var unpaid = await _repo.ListAsync<CardInvoice>(i => i.Status != InvoiceStatus.Paid, ct);
        var usedByCard = unpaid.GroupBy(i => i.CreditCardId).ToDictionary(g => g.Key, g => g.Sum(i => i.Total));
        return cards.OrderBy(c => c.Name).Select(c => ToResponse(c, usedByCard.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<CardResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var card = await RequireCardAsync(id, ct, track: false);
        return ToResponse(card, await UsedLimitAsync(id, ct));
    }

    public async Task<CardResponse> CreateAsync(CreateCardRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        await ValidatePaymentAccountAsync(request.PaymentAccountId, ct);
        var card = CreditCard.Create(request.Name, request.LimitTotal, request.ClosingDay, request.DueDay, request.PaymentAccountId);
        _repo.Add(card);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(card, 0);
    }

    public async Task<CardResponse> UpdateAsync(Guid id, CreateCardRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        await ValidatePaymentAccountAsync(request.PaymentAccountId, ct);
        var card = await RequireCardAsync(id, ct);
        card.Update(request.Name, request.LimitTotal, request.ClosingDay, request.DueDay, request.PaymentAccountId);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(card, await UsedLimitAsync(id, ct));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var card = await RequireCardAsync(id, ct);
        if (await _repo.AnyAsync<CardPurchase>(p => p.CreditCardId == id, ct))
            throw new ValidationException("id", "Cartão com compras não pode ser excluído.");

        _repo.SoftDelete(card);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CardPurchaseResponse>> ListPurchasesAsync(Guid cardId, string? competenceYm, CancellationToken ct)
    {
        var ym = string.IsNullOrWhiteSpace(competenceYm) ? null : Competence.Require(competenceYm);
        var items = await _repo.ListAsync<CardPurchase>(p => p.CreditCardId == cardId && (ym == null || p.CompetenceYm == ym), ct);
        return items.OrderByDescending(p => p.PurchasedAt).Select(ToResponse).ToList();
    }

    /// <summary>The purchase enters the budget in the purchase month; installments land on future invoices.</summary>
    public async Task<CardPurchaseResponse> AddPurchaseAsync(Guid cardId, CardPurchaseRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var card = await RequireCardAsync(cardId, ct, track: false);

        var category = await _repo.FirstOrDefaultAsync<Category>(c => c.Id == request.CategoryId, ct, track: false)
                       ?? throw new NotFoundException("Conta do plano", request.CategoryId);
        if (!category.AcceptsPosting
            || category.Section is not (CategorySection.Essential or CategorySection.Social or CategorySection.LifeProject or CategorySection.Discount))
            throw new ValidationException("categoryId", "Informe uma conta analítica de despesa ativa.");

        var purchase = CardPurchase.Create(card, request.Amount, request.PurchasedAt, request.Installments, request.CategoryId, request.Description);
        await _months.EnsureOpenAsync(purchase.CompetenceYm, ct);

        var used = await UsedLimitAsync(cardId, ct);
        if (used + purchase.Amount > card.LimitTotal)
            throw new ValidationException("amount", $"Limite insuficiente. Disponível: {card.LimitTotal - used:0.00}.");

        foreach (var (invoiceYm, amount) in purchase.InstallmentPlan())
        {
            var invoice = await _repo.FirstOrDefaultAsync<CardInvoice>(i => i.CreditCardId == cardId && i.CompetenceYm == invoiceYm, ct);
            if (invoice is null)
            {
                invoice = CardInvoice.Create(cardId, invoiceYm);
                _repo.Add(invoice);
            }
            else if (invoice.Status == InvoiceStatus.Paid)
            {
                throw new ValidationException("purchasedAt", $"A fatura de {invoiceYm} já foi paga.");
            }

            invoice.AddAmount(amount);
        }

        _repo.Add(purchase);
        AuditRecorder.Record(_audits, _correlation, "CardPurchase", purchase.Id, "CardPurchaseCreated",
            new { cardId, purchase.Amount, purchase.Installments, purchase.CompetenceYm });
        await _uow.SaveChangesAsync(ct);
        return ToResponse(purchase);
    }

    /// <summary>Refund (estorno): removes the purchase from its invoices. Not allowed once an invoice was paid.</summary>
    public async Task RefundPurchaseAsync(Guid cardId, Guid purchaseId, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var purchase = await _repo.FirstOrDefaultAsync<CardPurchase>(p => p.Id == purchaseId && p.CreditCardId == cardId, ct)
                       ?? throw new NotFoundException("Compra", purchaseId);
        await _months.EnsureOpenAsync(purchase.CompetenceYm, ct);

        foreach (var (invoiceYm, amount) in purchase.InstallmentPlan())
        {
            var invoice = await _repo.FirstOrDefaultAsync<CardInvoice>(i => i.CreditCardId == cardId && i.CompetenceYm == invoiceYm, ct);
            if (invoice is null)
                continue;
            if (invoice.Status == InvoiceStatus.Paid)
                throw new ValidationException("purchaseId", $"A fatura de {invoiceYm} já foi paga; lance o estorno como receita.");

            invoice.AddAmount(-amount);
        }

        _repo.SoftDelete(purchase);
        AuditRecorder.Record(_audits, _correlation, "CardPurchase", purchase.Id, "CardPurchaseRefunded",
            new { cardId, purchase.Amount, purchase.CompetenceYm });
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CardInvoiceResponse>> ListInvoicesAsync(Guid cardId, CancellationToken ct)
    {
        var card = await RequireCardAsync(cardId, ct, track: false);
        var invoices = await _repo.ListAsync<CardInvoice>(i => i.CreditCardId == cardId, ct);
        return invoices.OrderByDescending(i => i.CompetenceYm).Select(i => ToResponse(card, i)).ToList();
    }

    /// <summary>Paying an invoice creates a CardPayment entry (never an Expense) and settles the invoice.</summary>
    public async Task<CardInvoiceResponse> PayInvoiceAsync(Guid cardId, string invoiceYm, PayInvoiceRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        Competence.Require(invoiceYm, "invoiceYm");
        var card = await RequireCardAsync(cardId, ct, track: false);
        var invoice = await _repo.FirstOrDefaultAsync<CardInvoice>(i => i.CreditCardId == cardId && i.CompetenceYm == invoiceYm, ct)
                      ?? throw new NotFoundException($"Fatura {invoiceYm} não encontrada.");

        if (invoice.Status == InvoiceStatus.Paid)
            throw new ValidationException("invoiceYm", "A fatura já está paga.");
        if (invoice.Total <= 0)
            throw new ValidationException("invoiceYm", "A fatura não tem valor a pagar.");

        var accountId = request.AccountId ?? card.PaymentAccountId
                        ?? throw new ValidationException("accountId", "Informe a conta de pagamento.");
        var account = await _repo.FirstOrDefaultAsync<Account>(a => a.Id == accountId, ct, track: false)
                      ?? throw new NotFoundException("Conta", accountId);
        if (account.IsArchived)
            throw new ValidationException("accountId", "A conta está arquivada.");

        var paidAt = Competence.ToUtc(request.PaidAt ?? DateTime.UtcNow);
        await _months.EnsureOpenAsync(Competence.From(paidAt), ct);

        var payment = Entry.Create(
            EntryType.CardPayment,
            invoice.Total,
            paidAt,
            $"Pagamento da fatura {card.Name} {invoiceYm}",
            accountId: accountId,
            creditCardId: cardId);
        _repo.Add(payment);
        await EntryLedger.ApplyAsync(_repo, payment, 1, ct);
        invoice.MarkPaid();

        AuditRecorder.Record(_audits, _correlation, "CardInvoice", invoice.Id, "CardInvoicePaid",
            new { cardId, invoiceYm, invoice.Total, accountId });
        await _uow.SaveChangesAsync(ct);
        return ToResponse(card, invoice);
    }

    /// <summary>Used limit = every unpaid invoice total, including future installments.</summary>
    private async Task<decimal> UsedLimitAsync(Guid cardId, CancellationToken ct)
        => await _repo.SumAsync<CardInvoice>(i => i.CreditCardId == cardId && i.Status != InvoiceStatus.Paid, i => i.Total, ct);

    private async Task ValidatePaymentAccountAsync(Guid? accountId, CancellationToken ct)
    {
        if (accountId is Guid id && !await _repo.AnyAsync<Account>(a => a.Id == id, ct))
            throw new NotFoundException("Conta", id);
    }

    private async Task<CreditCard> RequireCardAsync(Guid id, CancellationToken ct, bool track = true)
        => await _repo.FirstOrDefaultAsync<CreditCard>(c => c.Id == id, ct, track) ?? throw new NotFoundException("Cartão", id);

    private static CardResponse ToResponse(CreditCard c, decimal used)
        => new(c.Id, c.Name, c.LimitTotal, c.ClosingDay, c.DueDay, c.PaymentAccountId, used, c.LimitTotal - used);

    private static CardPurchaseResponse ToResponse(CardPurchase p) => new(
        p.Id, p.CreditCardId, p.Amount, p.PurchasedAt, p.Installments, p.CategoryId, p.Description, p.CompetenceYm, p.FirstInvoiceYm);

    private static CardInvoiceResponse ToResponse(CreditCard card, CardInvoice invoice)
    {
        var start = Competence.Start(invoice.CompetenceYm);
        var days = DateTime.DaysInMonth(start.Year, start.Month);
        var closing = new DateOnly(start.Year, start.Month, Math.Min(card.ClosingDay, days));

        var dueMonth = card.DueDay <= card.ClosingDay ? start.AddMonths(1) : start;
        var dueDays = DateTime.DaysInMonth(dueMonth.Year, dueMonth.Month);
        var due = new DateOnly(dueMonth.Year, dueMonth.Month, Math.Min(card.DueDay, dueDays));

        var status = invoice.Status == InvoiceStatus.Paid
            ? InvoiceStatus.Paid
            : DateOnly.FromDateTime(DateTime.UtcNow) > closing ? InvoiceStatus.Closed : InvoiceStatus.Open;
        return new CardInvoiceResponse(card.Id, invoice.CompetenceYm, status, invoice.Total, closing, due);
    }
}
