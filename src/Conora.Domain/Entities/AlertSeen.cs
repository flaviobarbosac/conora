namespace Conora.Domain.Entities;

/// <summary>Marks a dashboard alert as seen for a user and competence. Counter counts only unseen.</summary>
public class AlertSeen : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string CompetenceYm { get; private set; } = default!;
    public string Code { get; private set; } = default!;
    public Guid? CategoryId { get; private set; }
    public DateTime SeenAt { get; private set; }

    private AlertSeen()
    {
    }

    public static AlertSeen Mark(string competenceYm, string code, Guid? categoryId, DateTime seenAt) => new()
    {
        CompetenceYm = competenceYm,
        Code = code,
        CategoryId = categoryId,
        SeenAt = seenAt
    };
}
