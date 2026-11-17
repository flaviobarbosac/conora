using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

/// <summary>
/// Console operators extend a tasting or grant product access. Duo and Família invites stay on a paid plan.
/// </summary>
public sealed class ConsoleAccessAdminService
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IAuditEventRepository _audits;
    private readonly ICorrelationContext _correlation;

    public ConsoleAccessAdminService(
        IUserRepository users,
        IUnitOfWork uow,
        IAuditEventRepository audits,
        ICorrelationContext correlation)
    {
        _users = users;
        _uow = uow;
        _audits = audits;
        _correlation = correlation;
    }

    public async Task<IReadOnlyList<ConsoleUserAccessResponse>> SearchAsync(string? term, CancellationToken ct)
    {
        var query = term?.Trim() ?? "";
        if (query.Length < 2)
            throw new ValidationException("term", "Informe ao menos 2 letras do nome ou do e-mail.");

        var users = await _users.SearchActiveAsync(query, 20, ct);
        return users.Select(ToResponse).ToList();
    }

    public async Task<ConsoleUserAccessResponse> SetTrialEndAsync(Guid userId, DateOnly endsOn, CancellationToken ct)
    {
        var user = await RequireUserAsync(userId, ct);
        var endsAt = AccessPolicy.BrazilDayEndsUtc(endsOn);
        user.SetTrialEndsAt(endsAt);
        AuditRecorder.Record(
            _audits,
            _correlation,
            "User",
            user.Id,
            "TrialEndSet",
            new { endsOn, endsAt });
        await _uow.SaveChangesAsync(ct);
        return ToResponse(user);
    }

    public async Task<ConsoleUserAccessResponse> SetGrantAsync(
        Guid userId,
        bool indefinite,
        DateOnly? until,
        CancellationToken ct)
    {
        if (!indefinite && until is null)
            throw new ValidationException("until", "Informe a data final ou marque o acesso sem data.");

        var user = await RequireUserAsync(userId, ct);
        DateTime? untilUtc = indefinite || until is null ? null : AccessPolicy.BrazilDayEndsUtc(until.Value);
        user.SetComplimentaryAccess(indefinite, untilUtc);
        AuditRecorder.Record(
            _audits,
            _correlation,
            "User",
            user.Id,
            indefinite ? "AccessGrantIndefinite" : "AccessGrantUntil",
            new { indefinite, until, untilUtc });
        await _uow.SaveChangesAsync(ct);
        return ToResponse(user);
    }

    public async Task<ConsoleUserAccessResponse> ClearGrantAsync(Guid userId, CancellationToken ct)
    {
        var user = await RequireUserAsync(userId, ct);
        user.SetComplimentaryAccess(false, null);
        AuditRecorder.Record(_audits, _correlation, "User", user.Id, "AccessGrantCleared", new { user.Email });
        await _uow.SaveChangesAsync(ct);
        return ToResponse(user);
    }

    private async Task<Domain.Entities.User> RequireUserAsync(Guid userId, CancellationToken ct)
        => await _users.FindActiveByIdAsync(userId, ct)
           ?? throw new NotFoundException("Usuário", userId);

    private static ConsoleUserAccessResponse ToResponse(Domain.Entities.User user)
        => new(
            user.Id,
            user.Name,
            user.Email,
            user.TrialEndsAtUtc,
            user.ComplimentaryAccessIndefinite,
            user.ComplimentaryAccessUntilUtc);
}
