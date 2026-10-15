using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

/// <summary>Budget-owner succession. Completing transfer moves group budget ownership.</summary>
public class FamilyOwnerTransfer : ModelBase
{
    public Guid FamilyGroupId { get; private set; }
    public Guid FromUsuarioId { get; private set; }
    public Guid ToUsuarioId { get; private set; }
    public string Token { get; private set; } = default!;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    public bool IsOpen => AcceptedAt is null && CancelledAt is null && ExpiresAt > DateTime.UtcNow;

    private FamilyOwnerTransfer()
    {
    }

    public static FamilyOwnerTransfer Create(
        Guid familyGroupId,
        Guid fromUsuarioId,
        Guid toUsuarioId,
        TimeSpan ttl)
    {
        if (fromUsuarioId == toUsuarioId)
            throw new ValidationException("toUsuarioId", "Escolha outro membro do grupo.");

        return new FamilyOwnerTransfer
        {
            FamilyGroupId = familyGroupId,
            FromUsuarioId = fromUsuarioId,
            ToUsuarioId = toUsuarioId,
            Token = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant(),
            ExpiresAt = DateTime.UtcNow.Add(ttl)
        };
    }

    public void Accept(Guid acceptorUsuarioId)
    {
        if (CancelledAt is not null)
            throw new ValidationException("token", "Esta transferência foi cancelada.");
        if (acceptorUsuarioId != ToUsuarioId)
            throw new ValidationException("token", "Só o membro convidado pode aceitar.");
        if (AcceptedAt is not null)
            return;
        if (ExpiresAt <= DateTime.UtcNow)
            throw new ValidationException("token", "Esta transferência não está mais aberta.");
        AcceptedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (AcceptedAt is not null)
            throw new ValidationException("transfer", "Transferência já aceita.");
        CancelledAt = DateTime.UtcNow;
    }
}
