using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

/// <summary>Internal label to mark who made an entry. Not a login (spec C13).</summary>
public class FamilyMember : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string Name { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;

    private FamilyMember()
    {
    }

    public static FamilyMember Create(string name)
    {
        var member = new FamilyMember();
        member.Update(name, true);
        return member;
    }

    public void Update(string name, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("name", "Nome é obrigatório.");

        Name = name.Trim();
        IsActive = isActive;
    }
}
