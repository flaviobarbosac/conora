namespace Conora.Domain.Entities;

/// <summary>In-app notice targeted at a user. Not tenant-stamped so the acceptor can notify the inviter.</summary>
public class FamilyNotice : ModelBase
{
    public Guid UsuarioId { get; private set; }
    public string Kind { get; private set; } = default!;
    public string Message { get; private set; } = default!;
    public DateTime? ReadAt { get; private set; }

    private FamilyNotice()
    {
    }

    public static FamilyNotice Create(Guid usuarioId, string kind, string message) => new()
    {
        UsuarioId = usuarioId,
        Kind = kind,
        Message = message.Trim()
    };

    public void MarkRead() => ReadAt ??= DateTime.UtcNow;
}
