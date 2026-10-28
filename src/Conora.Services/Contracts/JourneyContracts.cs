namespace Conora.Services.Contracts;

public sealed record JourneyTrackRequest(string Path, string Kind);

public sealed record JourneyAbandonmentRow(
    Guid UsuarioId,
    string Email,
    string Name,
    DateTime? FirstLoginAt,
    DateTime? LastSeenAt,
    string? FirstPath,
    string? LastPath,
    bool HasEntry,
    bool OnboardingCompleted,
    IReadOnlyList<string> PathTrail);
