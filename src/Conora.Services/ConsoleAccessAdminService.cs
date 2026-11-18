using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Infrastructure.Persistence;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Conora.Services;

/// <summary>
/// Console operators extend a tasting or grant product access. Duo and Família invites stay on a paid plan.
/// </summary>
public sealed class ConsoleAccessAdminService
{
    private readonly IUserRepository _users;
    private readonly AppDbContext _db;
    private readonly IUnitOfWork _uow;
    private readonly IAuditEventRepository _audits;
    private readonly ICorrelationContext _correlation;
    private readonly ITenantContext _tenant;

    public ConsoleAccessAdminService(
        IUserRepository users,
        AppDbContext db,
        IUnitOfWork uow,
        IAuditEventRepository audits,
        ICorrelationContext correlation,
        ITenantContext tenant)
    {
        _users = users;
        _db = db;
        _uow = uow;
        _audits = audits;
        _correlation = correlation;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ConsoleUserAccessResponse>> SearchAsync(string? term, CancellationToken ct)
    {
        var query = term?.Trim() ?? "";
        if (query.Length < 2)
            throw new ValidationException("term", "Informe ao menos 2 letras do nome ou do e-mail.");

        var users = await _users.SearchActiveAsync(query, 20, ct);
        return await RespondAsync(users, ct);
    }

    public async Task<IReadOnlyList<ConsoleUserAccessResponse>> ListAsync(CancellationToken ct)
    {
        var users = await _users.ListActiveByCreatedDescAsync(ct);
        return await RespondAsync(users, ct);
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
        return await RespondOneAsync(user, ct);
    }

    public async Task<ConsoleUserAccessResponse> ClearGrantAsync(Guid userId, CancellationToken ct)
    {
        var user = await RequireUserAsync(userId, ct);
        user.SetComplimentaryAccess(false, null);
        AuditRecorder.Record(_audits, _correlation, "User", user.Id, "AccessGrantCleared", new { user.Email });
        await _uow.SaveChangesAsync(ct);
        return await RespondOneAsync(user, ct);
    }

    public async Task<ConsoleUserAccessResponse> AssignPlanAsync(Guid userId, PlanKind plan, CancellationToken ct)
    {
        if (!PlanCatalog.IsSellable(plan))
            throw new ValidationException("plan", "Plano inválido.");

        var user = await RequireUserAsync(userId, ct);
        var now = DateTime.UtcNow;
        _tenant.AdoptClient(userId);
        var subscription = await _db.WorkspaceSubscriptions
            .FirstOrDefaultAsync(sub => sub.DeletedAt == null, ct);
        if (subscription is null)
        {
            subscription = WorkspaceSubscription.Activate(plan, now);
            _db.WorkspaceSubscriptions.Add(subscription);
        }
        else
        {
            subscription.Grant(plan, now);
        }

        AuditRecorder.Record(
            _audits,
            _correlation,
            "WorkspaceSubscription",
            subscription.Id,
            "PlanGrantedByConsole",
            new { plan, subscription.ExpiresAt });
        await _uow.SaveChangesAsync(ct);
        return await RespondOneAsync(user, ct);
    }

    private async Task<User> RequireUserAsync(Guid userId, CancellationToken ct)
        => await _users.FindActiveByIdAsync(userId, ct)
           ?? throw new NotFoundException("Usuário", userId);

    private async Task<IReadOnlyList<ConsoleUserAccessResponse>> RespondAsync(
        IReadOnlyList<User> users,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var ids = users.Select(user => user.Id).ToList();
        List<WorkspaceSubscription> subscriptions;
        if (ids.Count == 0)
            subscriptions = [];
        else
            subscriptions = await _db.WorkspaceSubscriptions.IgnoreQueryFilters().AsNoTracking()
                .Where(sub => sub.DeletedAt == null && ids.Contains(sub.UsuarioId))
                .ToListAsync(ct);
        var byUser = subscriptions.GroupBy(sub => sub.UsuarioId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var sharedWithLeomar = await SharedWithLeomarAsync(ids, ct);
        return users.Select(user =>
        {
            byUser.TryGetValue(user.Id, out var rows);
            var (plan, inTrial, tastingEnds) = Describe(user, rows ?? [], now);
            return ToResponse(user, plan, inTrial, tastingEnds, sharedWithLeomar.GetValueOrDefault(user.Id) ?? []);
        }).ToList();
    }

    private async Task<Dictionary<Guid, string[]>> SharedWithLeomarAsync(IReadOnlyList<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return [];

        string[] consultantEmails = [
            "lelcampos2009@gmail.com",
            "lelcampos34@gmail.com",
            "flavio.barbosa.vix@gmail.com",
            "flaviobarbosa.vix@gmail.com",
        ];
        var consultantIds = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(user => user.DeletedAt == null && consultantEmails.Contains(user.Email.ToLower()))
            .Select(user => user.Id)
            .ToListAsync(ct);
        if (consultantIds.Count == 0)
            return [];

        var shares = await _db.MonthShares.AsNoTracking()
            .Where(share => share.DeletedAt == null
                && consultantIds.Contains(share.ConsultantUsuarioId)
                && userIds.Contains(share.ClientUsuarioId))
            .Select(share => new { share.ClientUsuarioId, share.CompetenceYm })
            .ToListAsync(ct);
        return shares
            .GroupBy(share => share.ClientUsuarioId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(share => share.CompetenceYm).Distinct().Order().ToArray());
    }

    private async Task<ConsoleUserAccessResponse> RespondOneAsync(User user, CancellationToken ct)
    {
        var rows = await RespondAsync([user], ct);
        return rows[0];
    }

    private static (string Plan, bool InTrial, DateTime TastingEndsAt) Describe(
        User user,
        IReadOnlyList<WorkspaceSubscription> subscriptions,
        DateTime now)
    {
        var active = subscriptions.FirstOrDefault(sub => sub.IsWritable(now));
        var covering = active ?? subscriptions.OrderByDescending(sub => sub.ExpiresAt).FirstOrDefault();
        var plan = active is null ? "Gratuito" : PlanCatalog.DisplayLabel(active.Plan);
        var state = AccessPolicy.Evaluate(
            user.CreatedAt,
            covering,
            now,
            user.IsTrainingStudent,
            user.TrialEndsAtUtc,
            user.ComplimentaryAccessIndefinite,
            user.ComplimentaryAccessUntilUtc);
        var start = AccessPolicy.TrialStart(user.CreatedAt);
        var tastingEnds = user.TrialEndsAtUtc
            ?? start.AddDays(AccessPolicy.TrialDaysFor(user.IsTrainingStudent));
        return (plan, state.Phase == AccessPhase.Trial, tastingEnds);
    }

    private static ConsoleUserAccessResponse ToResponse(
        User user,
        string plan,
        bool inTrial,
        DateTime tastingEndsAt,
        IReadOnlyList<string> sharedMonths)
        => new(
            user.Id,
            user.Name,
            user.Email,
            user.TrialEndsAtUtc,
            user.ComplimentaryAccessIndefinite,
            user.ComplimentaryAccessUntilUtc,
            user.CreatedAt,
            plan,
            inTrial,
            tastingEndsAt,
            sharedMonths);
}
