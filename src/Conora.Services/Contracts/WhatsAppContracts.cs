using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record LinkWhatsAppRequest(string Phone);

public sealed record WhatsAppLinkResponse(string PhoneE164, DateTime LinkedAt);

public sealed record WhatsAppPairingResponse(string Code, string Text);

/// <summary>Parsed entry fields collected during a WhatsApp conversation before persist.</summary>
public sealed record WhatsAppParsedEntry(
    EntryType Type,
    decimal? Amount,
    string Description,
    DateTime OccurredAt);

/// <summary>In-progress WhatsApp conversation (session), never a finance draft waiting for the app.</summary>
public sealed record WhatsAppSessionState(
    string Step,
    EntryType Type,
    decimal? Amount,
    string Description,
    DateTime OccurredAt,
    Guid? CategoryId = null,
    string? CategoryName = null,
    Guid? AccountId = null,
    string? AccountName = null,
    /// <summary>Debts chosen to apply when the entry is persisted.</summary>
    IReadOnlyList<Guid>? OpenDebtIds = null,
    /// <summary>Current numbered options (category, account or debt).</summary>
    IReadOnlyList<Guid>? ChoiceIds = null,
    IReadOnlyList<string>? ChoiceLabels = null);

public static class WhatsAppSessionSteps
{
    public const string NeedAmount = "NeedAmount";
    public const string ConfirmAmount = "ConfirmAmount";
    public const string NeedCategory = "NeedCategory";
    public const string ConfirmCategory = "ConfirmCategory";
    public const string NeedAccount = "NeedAccount";
    public const string ConfirmAccount = "ConfirmAccount";
    public const string NeedDebtChoice = "NeedDebtChoice";
    public const string ConfirmLaunch = "ConfirmLaunch";
}

/// <summary>Text to send back to the sender via Cloud API when configured.</summary>
public sealed record WhatsAppReply(string PhoneE164, string Text);
