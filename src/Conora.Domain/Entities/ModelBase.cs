namespace Conora.Domain.Entities;

public abstract class ModelBase
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public long Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
