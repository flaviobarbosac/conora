namespace Conora.Domain.Entities;

public class RefreshToken : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string TokenHash { get; private set; } = default!;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    private RefreshToken()
    {
    }

    public static RefreshToken Create(string tokenHash, DateTime expiresAtUtc)
    {
        return new RefreshToken
        {
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    public bool IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTime.UtcNow;

    public void Revoke() => RevokedAtUtc = DateTime.UtcNow;
}
