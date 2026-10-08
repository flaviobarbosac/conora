using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record LinkWhatsAppRequest(string Phone);

public sealed record WhatsAppLinkResponse(string PhoneE164, DateTime LinkedAt);

public sealed record WhatsAppDraftResponse(Guid Id, string PhoneE164, string PayloadJson, WhatsAppDraftStatus Status, DateTime CreatedAt);

public sealed record ConfirmWhatsAppDraftRequest(Guid? AccountId = null, Guid? ChartAccountId = null);

/// <summary>Parsed payload stored in <c>WhatsAppDraft.PayloadJson</c>.</summary>
public sealed record WhatsAppDraftPayload(EntryType Type, decimal Amount, string Description, DateTime OccurredAt);

/// <summary>Text to send back to the sender (outbound delivery is a stub for now).</summary>
public sealed record WhatsAppReply(string PhoneE164, string Text);
