namespace Conora.Services.Contracts;

public sealed record AskAiRequest(string Question, string? CompetenceYm = null);

/// <summary>Available is false when Gemini is not configured or unreachable; the app keeps working.</summary>
public sealed record AskAiResponse(bool Available, string Answer);
