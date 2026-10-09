using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class CreditCard : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string Name { get; private set; } = default!;
    public decimal LimitTotal { get; private set; }
    public int ClosingDay { get; private set; }
    public int DueDay { get; private set; }
    public Guid? PaymentAccountId { get; private set; }

    private CreditCard()
    {
    }

    public static CreditCard Create(string name, decimal limitTotal, int closingDay, int dueDay, Guid? paymentAccountId)
    {
        var card = new CreditCard();
        card.Update(name, limitTotal, closingDay, dueDay, paymentAccountId);
        return card;
    }

    public void Update(string name, decimal limitTotal, int closingDay, int dueDay, Guid? paymentAccountId)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["Nome do cartão é obrigatório."];
        if (limitTotal < 0) errors["limitTotal"] = ["Limite não pode ser negativo."];
        if (closingDay is < 1 or > 31) errors["closingDay"] = ["Dia de fechamento deve estar entre 1 e 31."];
        if (dueDay is < 1 or > 31) errors["dueDay"] = ["Dia de vencimento deve estar entre 1 e 31."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        Name = name.Trim();
        LimitTotal = limitTotal;
        ClosingDay = closingDay;
        DueDay = dueDay;
        PaymentAccountId = paymentAccountId;
    }
}
