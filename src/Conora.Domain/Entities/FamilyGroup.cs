namespace Conora.Domain.Entities;

/// <summary>Shared family group. Not tenant-owned: members span UsuarioIds.</summary>
public class FamilyGroup : ModelBase
{
    public Guid CreatedByUsuarioId { get; private set; }

    private FamilyGroup()
    {
    }

    public static FamilyGroup Create(Guid createdByUsuarioId) => new()
    {
        CreatedByUsuarioId = createdByUsuarioId
    };
}
