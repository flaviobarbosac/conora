using Conora.Domain.Ports;

namespace Conora.Infrastructure.Security;

public sealed class TenantContext : ITenantContext
{
    public Guid? UsuarioId { get; private set; }
    public bool IsAuthenticated => UsuarioId.HasValue;

    public void Set(Guid usuarioId)
    {
        UsuarioId = usuarioId;
    }
}
