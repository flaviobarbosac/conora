namespace Conora.Domain.Entities;

/// <summary>Granted Ian maturity milestone. Never revoked once persisted.</summary>
public class UserMilestone : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string Key { get; private set; } = default!;
    public int Age { get; private set; }
    public int Points { get; private set; }
    public DateTime GrantedAt { get; private set; }

    private UserMilestone()
    {
    }

    public static UserMilestone Grant(string key, int age, int points, DateTime grantedAt) => new()
    {
        Key = key,
        Age = age,
        Points = points,
        GrantedAt = grantedAt
    };
}
