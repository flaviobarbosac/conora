using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Infrastructure.Billing;
using Conora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conora.Services;

/// <summary>Sends one e-mail when a tasting ends within 24 hours and the account is not subscribed.</summary>
public sealed class TrialEndingNoticeService
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly ILogger<TrialEndingNoticeService> _logger;
    private readonly long _hotmartProductId;

    public TrialEndingNoticeService(
        AppDbContext db,
        IEmailSender email,
        IConfiguration config,
        ILogger<TrialEndingNoticeService> logger,
        IOptions<HotmartOptions> hotmart)
    {
        _db = db;
        _email = email;
        _config = config;
        _logger = logger;
        _hotmartProductId = hotmart.Value.ProductId;
    }

    public async Task<int> SendDueAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var users = await _db.Users.IgnoreQueryFilters()
            .Where(user => user.DeletedAt == null)
            .ToListAsync(ct);
        if (users.Count == 0)
            return 0;

        var subscriptions = await _db.WorkspaceSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(sub => sub.DeletedAt == null)
            .ToListAsync(ct);
        var own = subscriptions
            .GroupBy(sub => sub.UsuarioId)
            .ToDictionary(
                group => group.Key,
                group => group.FirstOrDefault(sub => sub.IsWritable(now)) ?? group.OrderByDescending(sub => sub.ExpiresAt).First());

        var groups = await _db.FamilyGroups.AsNoTracking()
            .Where(group => group.DeletedAt == null)
            .ToDictionaryAsync(group => group.Id, ct);
        var members = await _db.FamilyGroupMembers.AsNoTracking()
            .Where(member => member.DeletedAt == null)
            .ToListAsync(ct);
        var memberGroup = members
            .GroupBy(member => member.UsuarioId)
            .ToDictionary(group => group.Key, group => group.First().FamilyGroupId);

        var courseEnds = await CourseEndsAsync(now, ct);
        var link = $"{PublicWebBase()}/app/plano";
        var sent = 0;

        foreach (var user in users)
        {
            own.TryGetValue(user.Id, out var subscription);
            if (CoveredByFamily(user.Id, own, groups, memberGroup, now) is WorkspaceSubscription familyPlan)
                subscription = familyPlan;

            courseEnds.TryGetValue(user.Email.Trim(), out var courseEnd);
            var isStudent = courseEnd is not null || user.IsTrainingStudent;
            var trialOverride = user.TrialEndsAtUtc ?? (isStudent ? courseEnd : null);
            var state = AccessPolicy.Evaluate(
                user.CreatedAt,
                subscription,
                now,
                isStudent,
                trialOverride,
                user.ComplimentaryAccessIndefinite,
                user.ComplimentaryAccessUntilUtc);

            if (state.Phase != AccessPhase.Trial || !AccessPolicy.IsInsideEndingDay(now, state.TrialEndsAt))
                continue;
            if (AlreadySent(user.TrialEndingNoticeForUtc, state.TrialEndsAt))
                continue;

            try
            {
                await _email.SendAsync(user.Email, "Sua degustação no Conora termina amanhã", Notice(user.Name, link), ct);
                user.MarkTrialEndingNotice(state.TrialEndsAt);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Trial ending notice failed for {Email}.", user.Email);
            }
        }

        if (sent > 0)
            await _db.SaveChangesAsync(ct);
        return sent;
    }

    private static WorkspaceSubscription? CoveredByFamily(
        Guid userId,
        IReadOnlyDictionary<Guid, WorkspaceSubscription> own,
        IReadOnlyDictionary<Guid, FamilyGroup> groups,
        IReadOnlyDictionary<Guid, Guid> memberGroup,
        DateTime now)
    {
        if (!memberGroup.TryGetValue(userId, out var groupId) || !groups.TryGetValue(groupId, out var group))
            return null;
        if (group.BudgetOwnerUsuarioId == userId)
            return null;
        if (!own.TryGetValue(group.BudgetOwnerUsuarioId, out var ownerPlan))
            return null;
        return ownerPlan.IsWritable(now) && ownerPlan.Tier != PlanTier.Individual ? ownerPlan : null;
    }

    private async Task<Dictionary<string, DateTime?>> CourseEndsAsync(DateTime now, CancellationToken ct)
    {
        var map = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
        if (_hotmartProductId <= 0)
            return map;

        var rows = await _db.HotmartCoursePurchases.AsNoTracking()
            .Where(purchase => purchase.ProductId == _hotmartProductId)
            .ToListAsync(ct);
        foreach (var group in rows.GroupBy(purchase => purchase.Email.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var unlock = group
                .Where(purchase => !purchase.IsRevoked)
                .Select(purchase => HotmartCoursePurchase.AccessAvailableAtUtc(purchase.AcquiredAt))
                .Where(at => at <= now)
                .OrderByDescending(at => at)
                .FirstOrDefault();
            map[group.Key] = unlock == default ? null : unlock.AddDays(AccessPolicy.StudentTrialDays);
        }

        return map;
    }

    private static bool AlreadySent(DateTime? sentFor, DateTime trialEnds)
        => sentFor is DateTime sent && Math.Abs((sent - trialEnds).TotalSeconds) < 2;

    private static EmailContent Notice(string name, string link) => new(
        $"{name}, sua degustação termina amanhã.",
        [
            "Falta um dia para o fim da sua degustação no Conora.",
            "Assinando agora, você continua com o orçamento, os lançamentos e o mês que já começou a organizar.",
            "Sem a assinatura, o acesso se encerra quando a degustação terminar. A gente adoraria que você seguisse."
        ])
    {
        Preheader = "Assine e continue de onde você parou.",
        Subtitle = "Vivendo bem o presente, planejando um futuro melhor.",
        ActionLabel = "Assinar o Conora",
        ActionUrl = link
    };

    private string PublicWebBase()
    {
        var configured = _config["App:PublicWebUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');
        var cors = _config["Cors:Origins:0"];
        if (!string.IsNullOrWhiteSpace(cors))
            return cors.TrimEnd('/');
        return "https://conora.com.br";
    }
}
