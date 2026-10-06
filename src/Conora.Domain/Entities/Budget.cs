using Conora.Domain.Enums;
using Conora.Domain.Services;

namespace Conora.Domain.Entities;

public class Budget : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string CompetenceYm { get; private set; } = default!;
    public BudgetMode Mode { get; private set; }

    private Budget()
    {
    }

    public static Budget Create(string competenceYm, BudgetMode mode) => new()
    {
        CompetenceYm = Competence.Require(competenceYm),
        Mode = mode
    };

    public void SetMode(BudgetMode mode) => Mode = mode;
}
