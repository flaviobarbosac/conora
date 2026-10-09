using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class FeedbackService
{
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ICorrelationContext _correlation;

    public FeedbackService(IAuditEventRepository audits, IUnitOfWork uow, ICorrelationContext correlation)
    {
        _audits = audits;
        _uow = uow;
        _correlation = correlation;
    }

    public async Task SubmitAsync(FeedbackRequest request, CancellationToken ct)
    {
        var tried = (request.Tried ?? string.Empty).Trim();
        var blocked = (request.Blocked ?? string.Empty).Trim();
        if (tried.Length is < 3 or > 2000)
            throw new ValidationException("tried", "Descreva o que tentou fazer (3 a 2000 caracteres).");
        if (blocked.Length is < 3 or > 2000)
            throw new ValidationException("blocked", "Descreva o que travou (3 a 2000 caracteres).");

        var id = Guid.NewGuid();
        AuditRecorder.Record(_audits, _correlation, "UserFeedback", id, "FeedbackSubmitted", new
        {
            Tried = tried,
            Blocked = blocked,
        });
        await _uow.SaveChangesAsync(ct);
    }
}
