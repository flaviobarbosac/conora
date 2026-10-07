namespace Conora.Domain.Entities;

/// <summary>Active membership in a family group. One active membership per user.</summary>
public class FamilyGroupMember : ModelBase
{
    public Guid FamilyGroupId { get; private set; }
    public Guid UsuarioId { get; private set; }

    private FamilyGroupMember()
    {
    }

    public static FamilyGroupMember Create(Guid familyGroupId, Guid usuarioId) => new()
    {
        FamilyGroupId = familyGroupId,
        UsuarioId = usuarioId
    };
}
