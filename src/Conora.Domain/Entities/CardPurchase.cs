using Conora.Domain.Exceptions;
using Conora.Domain.Services;

namespace Conora.Domain.Entities;

public class CardPurchase : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public Guid CreditCardId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime PurchasedAt { get; private set; }
    public int Installments { get; private set; }
    public Guid CategoryId { get; private set; }
    public string Description { get; private set; } = default!;

    /// <summary>Budget competence = purchase month (spec v1.1 §2).</summary>
    public string CompetenceYm { get; private set; } = default!;

    /// <summary>Invoice competence of the first installment (depends on the card closing day).</summary>
    public string FirstInvoiceYm { get; private set; } = default!;

    private CardPurchase()
    {
    }

    public static CardPurchase Create(
        CreditCard card,
        decimal amount,
        DateTime purchasedAt,
        int installments,
        Guid categoryId,
        string description)
    {
        var errors = new Dictionary<string, string[]>();
        if (amount <= 0) errors["amount"] = ["Valor deve ser maior que zero."];
        if (installments is < 1 or > 60) errors["installments"] = ["Parcelas devem estar entre 1 e 60."];
        if (string.IsNullOrWhiteSpace(description)) errors["description"] = ["Descrição é obrigatória."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        var date = Competence.ToUtc(purchasedAt);
        var purchaseYm = Competence.From(date);
        return new CardPurchase
        {
            CreditCardId = card.Id,
            Amount = decimal.Round(amount, 2),
            PurchasedAt = date,
            Installments = installments,
            CategoryId = categoryId,
            Description = description.Trim(),
            CompetenceYm = purchaseYm,
            FirstInvoiceYm = date.Day > card.ClosingDay ? Competence.AddMonths(purchaseYm, 1) : purchaseYm
        };
    }

    /// <summary>Splits the total in installments; the last one absorbs the rounding remainder.</summary>
    public IReadOnlyList<(string InvoiceYm, decimal Amount)> InstallmentPlan()
    {
        var each = decimal.Round(Amount / Installments, 2);
        var plan = new List<(string, decimal)>(Installments);
        var allocated = 0m;
        for (var i = 0; i < Installments; i++)
        {
            var value = i == Installments - 1 ? Amount - allocated : each;
            allocated += value;
            plan.Add((Competence.AddMonths(FirstInvoiceYm, i), value));
        }

        return plan;
    }
}
