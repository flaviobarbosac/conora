namespace Conora.Services.Contracts;

public sealed record SpeakRequest(string Text, string? Voice);

public sealed record VoiceCommandRequest(string Transcript, string? CompetenceYm = null);

/// <summary>
/// Classified spoken command. The client navigates and saves; this response never does either.
/// Intent: navigate | action | ask. Action today: create-entry.
/// </summary>
public sealed record VoiceCommandResponse(
    string Intent,
    string? Route,
    string? Action,
    VoiceEntryDraft? Entry,
    string Reply,
    IanReportQuery? Report = null);

public sealed record VoiceEntryDraft(
    string Type,
    decimal? Amount,
    string? Description,
    string? CategoryName,
    string? AccountName,
    string? Date = null);
