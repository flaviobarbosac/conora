using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class FamilyInvite : ModelBase
{
    public Guid InviterUsuarioId { get; private set; }
    public Guid? FamilyGroupId { get; private set; }
    public string Email { get; private set; } = default!;
    public string Token { get; private set; } = default!;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public Guid? AcceptedByUsuarioId { get; private set; }

    private FamilyInvite()
    {
    }

    public static FamilyInvite Create(Guid inviterUsuarioId, string email, Guid? familyGroupId, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ValidationException("email", "E-mail do convidado é obrigatório.");

        return new FamilyInvite
        {
            InviterUsuarioId = inviterUsuarioId,
            FamilyGroupId = familyGroupId,
            Email = email.Trim().ToLowerInvariant(),
            Token = Convert.ToHexString(Guid.CreateVersion7().ToByteArray()) + Convert.ToHexString(Guid.CreateVersion7().ToByteArray()),
            ExpiresAt = DateTime.UtcNow.Add(ttl)
        };
    }

    public void BindGroup(Guid familyGroupId) => FamilyGroupId = familyGroupId;

    public void Cancel()
    {
        if (AcceptedAt is not null)
            throw new ValidationException("invite", "Convite já aceito não pode ser cancelado.");
        CancelledAt = DateTime.UtcNow;
    }

    public void MarkAccepted(Guid usuarioId)
    {
        AcceptedAt = DateTime.UtcNow;
        AcceptedByUsuarioId = usuarioId;
    }

    public bool IsOpen => AcceptedAt is null && CancelledAt is null && ExpiresAt > DateTime.UtcNow;
}
