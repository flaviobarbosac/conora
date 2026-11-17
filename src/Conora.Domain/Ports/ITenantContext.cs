namespace Conora.Domain.Ports;

public interface ITenantContext
{
    Guid? UsuarioId { get; }
    bool IsAuthenticated { get; }
    void Set(Guid usuarioId);

    /// <summary>Moves the request onto the client after a month share was confirmed. Keeps the consultant as actor elsewhere.</summary>
    void AdoptClient(Guid clientUsuarioId);
}
