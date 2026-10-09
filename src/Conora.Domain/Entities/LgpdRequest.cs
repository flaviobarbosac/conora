namespace Conora.Domain.Entities;

public class LgpdRequest : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string Kind { get; private set; } = default!;
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    private LgpdRequest()
    {
    }

    public static LgpdRequest Export() => New("export");

    public static LgpdRequest Deletion() => New("deletion");

    public void Complete() => CompletedAtUtc = DateTime.UtcNow;

    private static LgpdRequest New(string kind) => new()
    {
        Kind = kind,
        RequestedAtUtc = DateTime.UtcNow
    };
}
