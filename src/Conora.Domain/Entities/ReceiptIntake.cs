using Conora.Domain.Enums;
using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class ReceiptIntake : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public ReceiptChannel Channel { get; private set; }
    public string ImageObjectKey { get; private set; } = default!;
    public ReceiptIntakeStatus Status { get; private set; }
    public string? ExtractedCnpj { get; private set; }
    public string? ExtractedName { get; private set; }
    public decimal? ExtractedAmount { get; private set; }
    public DateTime? ExtractedOccurredAt { get; private set; }
    public Guid? SupplierId { get; private set; }
    public string? ProposedCategoryCode { get; private set; }
    public Guid? SelectedBankAccountId { get; private set; }
    public Guid? WhatsAppDraftId { get; private set; }
    public string? PhoneE164 { get; private set; }
    public WhatsAppReceiptAwaiting WhatsAppAwaiting { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime ImageExpiresAt { get; private set; }

    private ReceiptIntake()
    {
    }

    public static ReceiptIntake Create(ReceiptChannel channel, string imageObjectKey, TimeSpan imageTtl, string? phoneE164 = null)
        => new()
        {
            Channel = channel,
            ImageObjectKey = imageObjectKey,
            Status = ReceiptIntakeStatus.Processing,
            PhoneE164 = phoneE164,
            WhatsAppAwaiting = WhatsAppReceiptAwaiting.None,
            ImageExpiresAt = DateTime.UtcNow.Add(imageTtl)
        };

    public void MarkFailed(string reason)
    {
        Status = ReceiptIntakeStatus.Failed;
        FailureReason = reason.Length > 500 ? reason[..500] : reason;
        WhatsAppAwaiting = WhatsAppReceiptAwaiting.None;
    }

    public void ApplyOcr(string? cnpj, string? name, decimal? amount, DateTime? occurredAt, Guid? supplierId, string? proposedCategoryCode)
    {
        ExtractedCnpj = cnpj;
        ExtractedName = name;
        ExtractedAmount = amount;
        ExtractedOccurredAt = occurredAt;
        SupplierId = supplierId;
        ProposedCategoryCode = proposedCategoryCode;

        if (string.IsNullOrWhiteSpace(proposedCategoryCode))
        {
            Status = ReceiptIntakeStatus.NeedsClassification;
            WhatsAppAwaiting = Channel == ReceiptChannel.WhatsApp
                ? WhatsAppReceiptAwaiting.Classification
                : WhatsAppReceiptAwaiting.None;
        }
        else
        {
            Status = ReceiptIntakeStatus.Ready;
            WhatsAppAwaiting = Channel == ReceiptChannel.WhatsApp
                ? WhatsAppReceiptAwaiting.Confirm
                : WhatsAppReceiptAwaiting.None;
        }
    }

    public void SetClassification(string categoryCode, Guid? supplierId = null)
    {
        if (Status is not (ReceiptIntakeStatus.NeedsClassification or ReceiptIntakeStatus.Ready or ReceiptIntakeStatus.Processing))
            throw new ValidationException("status", "Este recibo não pode ser classificado agora.");

        ProposedCategoryCode = categoryCode.Trim().ToUpperInvariant();
        if (supplierId is Guid sid)
            SupplierId = sid;
        Status = ReceiptIntakeStatus.Ready;
        WhatsAppAwaiting = Channel == ReceiptChannel.WhatsApp
            ? WhatsAppReceiptAwaiting.Confirm
            : WhatsAppReceiptAwaiting.None;
    }

    public void ReopenClassification()
    {
        if (Status is not (ReceiptIntakeStatus.Ready or ReceiptIntakeStatus.NeedsClassification))
            throw new ValidationException("status", "Não é possível alterar a classificação agora.");

        Status = ReceiptIntakeStatus.NeedsClassification;
        WhatsAppAwaiting = Channel == ReceiptChannel.WhatsApp
            ? WhatsAppReceiptAwaiting.Classification
            : WhatsAppReceiptAwaiting.None;
    }

    public void SetBankAccount(Guid? accountId) => SelectedBankAccountId = accountId;

    public void LinkWhatsAppDraft(Guid draftId) => WhatsAppDraftId = draftId;

    public void Confirm()
    {
        if (Status != ReceiptIntakeStatus.Ready)
            throw new ValidationException("status", "Confirme só após a classificação.");

        Status = ReceiptIntakeStatus.Confirmed;
        WhatsAppAwaiting = WhatsAppReceiptAwaiting.None;
    }

    public void ClearImageKey() => ImageObjectKey = string.Empty;
}
