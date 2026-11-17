using Conora.Domain.Exceptions;
using Conora.Domain.Ports;

namespace Conora.Domain.Services;

public sealed class ConsultantMonthScope : IConsultantScope
{
    public bool IsActive { get; private set; }
    public Guid? ActorUsuarioId { get; private set; }
    public string? ActorName { get; private set; }
    public string? CompetenceYm { get; private set; }

    public void Activate(Guid actorUsuarioId, string competenceYm, string actorName)
    {
        if (actorUsuarioId == Guid.Empty)
            throw new ForbiddenException("Consultor inválido.");

        ActorUsuarioId = actorUsuarioId;
        ActorName = string.IsNullOrWhiteSpace(actorName) ? "Consultor" : actorName.Trim();
        CompetenceYm = Competence.Require(competenceYm);
        IsActive = true;
    }

    public void Ensure(string competenceYm)
    {
        if (!IsActive)
            return;

        var ym = Competence.Require(competenceYm);
        if (!string.Equals(ym, CompetenceYm, StringComparison.Ordinal))
            throw new ForbiddenException("Fora do mês compartilhado.");
    }

    public void EnsureAll(IEnumerable<string> competences)
    {
        foreach (var ym in competences)
            Ensure(ym);
    }
}
