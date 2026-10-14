using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

/// <summary>Shared family group. Not tenant-owned: members span UsuarioIds.</summary>
public class FamilyGroup : ModelBase
{
    public const int MaxMembers = 20;

    public Guid CreatedByUsuarioId { get; private set; }
    public Guid BudgetOwnerUsuarioId { get; private set; }

    private FamilyGroup()
    {
    }

    public static FamilyGroup Create(Guid createdByUsuarioId) => new()
    {
        CreatedByUsuarioId = createdByUsuarioId,
        BudgetOwnerUsuarioId = createdByUsuarioId
    };

    public bool IsBudgetOwner(Guid usuarioId) => BudgetOwnerUsuarioId == usuarioId;

    public void TransferBudgetOwner(Guid toUsuarioId)
    {
        if (toUsuarioId == Guid.Empty)
            throw new ValidationException("toUsuarioId", "Membro inválido.");
        BudgetOwnerUsuarioId = toUsuarioId;
    }
}
