namespace Conora.Services.Contracts;

public sealed record SpeakRequest(string Text, string? Voice, string? Band = null);

public sealed record VoiceTurn(string Role, string Text);

public sealed record SuggestCategoriesRequest(string Term, string? Type);

public sealed record SuggestCategoriesResponse(IReadOnlyList<string> Names);

public sealed record VoiceUserResponse(string Name, string FirstName);

public sealed record VoiceCommandRequest(
    string Transcript,
    string? CompetenceYm = null,
    IReadOnlyList<VoiceTurn>? History = null);

/// <summary>
/// Classified spoken command. The client navigates and saves; this response never does either.
/// Intent: navigate | action | ask. Action today: create-entry.
/// </summary>
public sealed record VoiceCommandResponse(
    string Intent,
    string? Route,
    string? Action,
    VoiceEntryDraft? Entry,
    string Reply);

public sealed record VoiceEntryDraft(
    string Type,
    decimal? Amount,
    string? Description,
    string? CategoryName,
    string? AccountName,
    string? Date = null);
