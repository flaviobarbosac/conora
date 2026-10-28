using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

/// <summary>Records free-tier navigation breadcrumbs for the abandonment console.</summary>
public sealed class JourneyService
{
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ICorrelationContext _correlation;
    private readonly ITenantContext _tenant;

    public JourneyService(
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        ITenantContext tenant)
    {
        _audits = audits;
        _uow = uow;
        _correlation = correlation;
        _tenant = tenant;
    }

    public async Task TrackAsync(JourneyTrackRequest request, CancellationToken ct)
    {
        var userId = _tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");
        var path = (request.Path ?? "/").Trim();
        if (path.Length > 200)
            path = path[..200];
        var kind = string.IsNullOrWhiteSpace(request.Kind) ? "Navigate" : request.Kind.Trim();
        if (kind.Length > 40)
            kind = kind[..40];

        AuditRecorder.Record(_audits, _correlation, "Journey", userId, kind, new { path });
        await _uow.SaveChangesAsync(ct);
    }
}
