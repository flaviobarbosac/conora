using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class FamilyInvite : ModelBase
{
    public Guid InviterUsuarioId { get; private set; }
    public Guid? FamilyGroupId { get; private set; }
    /// <summary>Empty for QR invites (presence join); e-mail invites store the invitee address.</summary>
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
            Token = NewToken(),
            ExpiresAt = DateTime.UtcNow.Add(ttl)
        };
    }

    public static FamilyInvite CreateQr(Guid inviterUsuarioId, Guid? familyGroupId, TimeSpan ttl)
    {
        return new FamilyInvite
        {
            InviterUsuarioId = inviterUsuarioId,
            FamilyGroupId = familyGroupId,
            Email = string.Empty,
            Token = NewToken(),
            ExpiresAt = DateTime.UtcNow.Add(ttl)
        };
    }

    public bool IsQrInvite => string.IsNullOrEmpty(Email);

    public void BindGroup(Guid familyGroupId) => FamilyGroupId = familyGroupId;

    public void ReassignInviter(Guid usuarioId)
    {
        if (usuarioId == Guid.Empty)
            throw new ValidationException("usuarioId", "Membro inválido.");
        if (AcceptedAt is not null || CancelledAt is not null)
            return;
        InviterUsuarioId = usuarioId;
    }

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

    private static string NewToken()
        => Convert.ToHexString(Guid.CreateVersion7().ToByteArray()) + Convert.ToHexString(Guid.CreateVersion7().ToByteArray());
}
