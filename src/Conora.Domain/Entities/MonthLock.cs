using Conora.Domain.Exceptions;
using Conora.Domain.Services;

namespace Conora.Domain.Entities;

public class MonthLock : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string CompetenceYm { get; private set; } = default!;
    public bool IsClosed { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public string? ReopenReason { get; private set; }
    public Guid ClosedByUsuarioId { get; private set; }

    private MonthLock()
    {
    }

    public static MonthLock Close(string competenceYm, Guid usuarioId)
    {
        var lockRow = new MonthLock { CompetenceYm = Competence.Require(competenceYm) };
        lockRow.CloseMonth(usuarioId);
        return lockRow;
    }

    public void CloseMonth(Guid usuarioId)
    {
        IsClosed = true;
        ClosedAt = DateTime.UtcNow;
        ClosedByUsuarioId = usuarioId;
        ReopenReason = null;
    }

    public void Reopen(string reason, Guid usuarioId)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ValidationException("reason", "Informe o motivo da reabertura (mínimo 5 caracteres).");

        if (ClosedByUsuarioId != usuarioId)
            throw new ForbiddenException("Somente o titular pode reabrir o mês.");

        IsClosed = false;
        ReopenReason = reason.Trim();
    }
}
