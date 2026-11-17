using Conora.Domain.Exceptions;
using Conora.Domain.Services;

namespace Conora.Domain.Entities;

/// <summary>
/// One competence the client opened for one consultant. Not tenant-owned: both sides are stored explicitly.
/// </summary>
public class MonthShare : ModelBase
{
    public Guid ClientUsuarioId { get; private set; }
    public Guid ConsultantUsuarioId { get; private set; }
    public string CompetenceYm { get; private set; } = default!;

    private MonthShare()
    {
    }

    public static MonthShare Open(Guid clientUsuarioId, Guid consultantUsuarioId, string competenceYm)
    {
        if (clientUsuarioId == Guid.Empty || consultantUsuarioId == Guid.Empty)
            throw new ValidationException("usuario", "Usuário inválido.");
        if (clientUsuarioId == consultantUsuarioId)
            throw new ValidationException("email", "Compartilhe com outro usuário.");

        return new MonthShare
        {
            ClientUsuarioId = clientUsuarioId,
            ConsultantUsuarioId = consultantUsuarioId,
            CompetenceYm = Competence.Require(competenceYm)
        };
    }
}
