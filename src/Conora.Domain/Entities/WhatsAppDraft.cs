using Conora.Domain.Enums;
using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class WhatsAppDraft : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string PhoneE164 { get; private set; } = default!;
    public string PayloadJson { get; private set; } = default!;
    public WhatsAppDraftStatus Status { get; private set; }

    private WhatsAppDraft()
    {
    }

    public static WhatsAppDraft Create(string phoneE164, string payloadJson) => new()
    {
        PhoneE164 = phoneE164,
        PayloadJson = payloadJson,
        Status = WhatsAppDraftStatus.Pending
    };

    public void Confirm()
    {
        EnsurePending();
        Status = WhatsAppDraftStatus.Confirmed;
    }

    public void Discard()
    {
        EnsurePending();
        Status = WhatsAppDraftStatus.Discarded;
    }

    private void EnsurePending()
    {
        if (Status != WhatsAppDraftStatus.Pending)
            throw new ValidationException("status", "O rascunho já foi processado.");
    }
}
