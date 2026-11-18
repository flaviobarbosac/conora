using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class MonthShareService
{
    private readonly IMonthShareRepository _shares;
    private readonly IUserRepository _users;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;

    public MonthShareService(
        IMonthShareRepository shares,
        IUserRepository users,
        ITenantContext tenant,
        IUnitOfWork uow)
    {
        _shares = shares;
        _users = users;
        _tenant = tenant;
        _uow = uow;
    }

    public async Task<MonthShareResponse> ShareAsync(string competenceYm, string email, CancellationToken ct)
    {
        var clientId = _tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");
        var ym = Competence.Require(competenceYm);
        var consultant = await RequireConsultantAsync(email, clientId, ct);
        var existing = await _shares.FindAsync(clientId, consultant.Id, ym, ct);
        if (existing is not null)
            return ToResponse(existing, consultant.Email);

        var share = MonthShare.Open(clientId, consultant.Id, ym);
        _shares.Add(share);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(share, consultant.Email);
    }

    public async Task ShareWithOperatorsAsync(string competenceYm, CancellationToken ct)
    {
        var clientId = _tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");
        var ym = Competence.Require(competenceYm);
        var added = false;
        foreach (var email in ConsoleAccess.OperatorEmails)
        {
            var consultant = await _users.FindByEmailAsync(email, ct);
            if (consultant is null || consultant.Id == clientId)
                continue;
            if (await _shares.FindAsync(clientId, consultant.Id, ym, ct) is not null)
                continue;
            _shares.Add(MonthShare.Open(clientId, consultant.Id, ym));
            added = true;
        }

        if (added)
            await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ListMineAsync(CancellationToken ct)
    {
        var clientId = _tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");
        var operatorIds = await OperatorIdsAsync(ct);
        var rows = await _shares.ListForClientAsync(clientId, ct);
        return rows
            .Where(share => operatorIds.Contains(share.ConsultantUsuarioId))
            .Select(share => share.CompetenceYm)
            .Distinct()
            .Order()
            .ToList();
    }

    public async Task UnshareAsync(string competenceYm, CancellationToken ct)
    {
        var clientId = _tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");
        var ym = Competence.Require(competenceYm);
        var operatorIds = await OperatorIdsAsync(ct);
        var rows = await _shares.ListForClientAsync(clientId, ct);
        var now = DateTime.UtcNow;
        var changed = false;
        foreach (var share in rows.Where(share => share.CompetenceYm == ym && operatorIds.Contains(share.ConsultantUsuarioId)))
        {
            share.DeletedAt = now;
            changed = true;
        }

        if (changed)
            await _uow.SaveChangesAsync(ct);
    }

    private async Task<HashSet<Guid>> OperatorIdsAsync(CancellationToken ct)
    {
        var ids = new HashSet<Guid>();
        foreach (var email in ConsoleAccess.OperatorEmails)
        {
            var consultant = await _users.FindByEmailAsync(email, ct);
            if (consultant is not null)
                ids.Add(consultant.Id);
        }

        return ids;
    }

    public async Task<IReadOnlyList<SharedMonthClientResponse>> ListSharedWithMeAsync(CancellationToken ct)
    {
        var consultantId = _tenant.UsuarioId ?? throw new ForbiddenException("Usuário não autenticado.");
        var rows = await _shares.ListForConsultantAsync(consultantId, ct);
        return rows
            .GroupBy(row => row.ClientUsuarioId)
            .Select(group =>
            {
                var first = group.First();
                var months = group.Select(row => row.CompetenceYm).Distinct().OrderBy(ym => ym).ToList();
                return new SharedMonthClientResponse(first.ClientUsuarioId, first.Name, first.Email, months);
            })
            .OrderBy(client => client.Name)
            .ToList();
    }

    private async Task<User> RequireConsultantAsync(string email, Guid clientId, CancellationToken ct)
    {
        if (!ConsoleAccess.IsAllowed(email))
            throw new ValidationException("email", "Esse e-mail não é de um consultor.");

        var consultant = await _users.FindByEmailAsync(email.Trim(), ct)
                          ?? throw new ValidationException("email", "Não encontramos um usuário com esse e-mail.");
        if (consultant.Id == clientId)
            throw new ValidationException("email", "Compartilhe com outro usuário.");
        return consultant;
    }

    private static MonthShareResponse ToResponse(MonthShare share, string email)
        => new(share.Id, share.ClientUsuarioId, share.ConsultantUsuarioId, email, share.CompetenceYm);
}

/// <summary>Opens the client tenant only when a month share exists. Refuses every other competence.</summary>
public sealed class ConsultantAccessService
{
    private readonly IMonthShareRepository _shares;
    private readonly IUserRepository _users;
    private readonly ITenantContext _tenant;
    private readonly IConsultantScope _scope;

    public ConsultantAccessService(
        IMonthShareRepository shares,
        IUserRepository users,
        ITenantContext tenant,
        IConsultantScope scope)
    {
        _shares = shares;
        _users = users;
        _tenant = tenant;
        _scope = scope;
    }

    public async Task EnterAsync(Guid clientUsuarioId, string competenceYm, CancellationToken ct)
    {
        var actorId = _tenant.UsuarioId ?? throw new UnauthorizedAccessException("Usuário não autenticado.");
        if (actorId == clientUsuarioId)
            return;

        var ym = Competence.Require(competenceYm);
        var share = await _shares.FindAsync(clientUsuarioId, actorId, ym, ct)
                    ?? throw new ForbiddenException("Esse mês não foi compartilhado com você.");
        var actor = await _users.FindActiveByIdAsync(actorId, ct);
        _tenant.AdoptClient(share.ClientUsuarioId);
        _scope.Activate(actorId, share.CompetenceYm, actor?.Name ?? "Consultor");
    }
}
