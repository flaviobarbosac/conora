namespace Conora.Domain.Ports;

/// <summary>
/// Active only after a month share is confirmed. Inside that month the consultant uses the
/// whole product (entries, budget, cards, accounts, categories, Raio-X, patrimony, milestones,
/// registration). Any other competence is refused.
/// </summary>
public interface IConsultantScope
{
    bool IsActive { get; }
    Guid? ActorUsuarioId { get; }
    string? ActorName { get; }
    string? CompetenceYm { get; }

    void Activate(Guid actorUsuarioId, string competenceYm, string actorName);
    void Ensure(string competenceYm);
    void EnsureAll(IEnumerable<string> competences);
}
