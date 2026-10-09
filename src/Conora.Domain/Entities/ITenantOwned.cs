namespace Conora.Domain.Entities;

public interface ITenantOwned
{
    Guid UsuarioId { get; set; }
}
