namespace Conora.Domain.Ports;

public interface ITenantContext
{
    Guid? UsuarioId { get; }
    bool IsAuthenticated { get; }
    void Set(Guid usuarioId);
}
