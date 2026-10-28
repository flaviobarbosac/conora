namespace Conora.Repository.Interface;

public interface IConsoleRepository
{
    Task<ConsoleReadModel> ReadAsync(CancellationToken ct);

    /// <summary>Last 30 days of Journey audit breadcrumbs for free-tier retention.</summary>
    Task<IReadOnlyList<JourneyAbandonmentReadModel>> ListJourneyAbandonmentAsync(CancellationToken ct);
}

public sealed record JourneyAbandonmentReadModel(
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
