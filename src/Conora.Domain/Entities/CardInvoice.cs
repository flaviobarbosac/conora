using Conora.Domain.Enums;
using Conora.Domain.Services;

namespace Conora.Domain.Entities;

public class CardInvoice : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public Guid CreditCardId { get; private set; }
    public string CompetenceYm { get; private set; } = default!;
    public InvoiceStatus Status { get; private set; }
    public decimal Total { get; private set; }

    private CardInvoice()
    {
    }

    public static CardInvoice Create(Guid creditCardId, string competenceYm) => new()
    {
        CreditCardId = creditCardId,
        CompetenceYm = Competence.Require(competenceYm),
        Status = InvoiceStatus.Open
    };

    public void AddAmount(decimal delta) => Total = Math.Max(0, Total + delta);

    public void Close()
    {
        if (Status == InvoiceStatus.Open)
            Status = InvoiceStatus.Closed;
    }

    public void MarkPaid() => Status = InvoiceStatus.Paid;
}
