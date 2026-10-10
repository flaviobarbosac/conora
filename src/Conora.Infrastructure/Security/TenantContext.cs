using Conora.Domain.Ports;

namespace Conora.Infrastructure.Security;

public sealed class TenantContext : ITenantContext
{
    public Guid? UsuarioId { get; private set; }
    public bool IsAuthenticated => UsuarioId.HasValue;

    public void Set(Guid usuarioId)
    {
        if (UsuarioId is Guid current && current != usuarioId)
            throw new InvalidOperationException("Tenant already bound for this request.");

        UsuarioId = usuarioId;
    }
}
