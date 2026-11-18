using System.Globalization;
using Conora.Domain.Entities;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace Conora.Services;

/// <summary>
/// After month close: one conversational insight to the bell + push. Does not stage debts.
/// </summary>
public sealed class MonthCloseCoachService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly BudgetService _budgets;
    private readonly PendingDebtService _debts;
    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly PushNotificationService _push;
    private readonly ILogger<MonthCloseCoachService> _logger;

    public MonthCloseCoachService(
        BudgetService budgets,
        PendingDebtService debts,
        IFinanceRepository repo,
        IUnitOfWork uow,
        PushNotificationService push,
        ILogger<MonthCloseCoachService> logger)
    {
        _budgets = budgets;
        _debts = debts;
        _repo = repo;
        _uow = uow;
        _push = push;
        _logger = logger;
    }

    public async Task NotifyIfNeededAsync(string competenceYm, CancellationToken ct)
    {
        var ym = Competence.Require(competenceYm);
        var already = await _repo.AnyAsync<AlertSeen>(
            s => s.CompetenceYm == ym && s.Code == MonthCloseCoach.SentCode, ct);
        if (already)
            return;

        var budget = await _budgets.GetAsync(ym, ct);
        var previousYm = Competence.AddMonths(ym, -1);
        var previousActuals = await _budgets.GetActualsAsync(previousYm, ct);
        var insight = MonthCloseCoach.Pick(
            MonthLabel(ym),
            MonthCloseCoach.FromBudget(budget),
            previousActuals,
            PtBr);
        if (insight is null)
            return;

        _repo.Add(AlertSeen.Mark(ym, MonthCloseCoach.SentCode, null, DateTime.UtcNow));
        await _uow.SaveChangesAsync(ct);

        try
        {
            await _push.SendAsync(
                new SendPushRequest(
                    "Conora",
                    insight.Message,
                    new Dictionary<string, string>
                    {
                        ["type"] = "month_close_coach",
                        ["competenceYm"] = ym,
                        ["code"] = MonthCloseCoach.AlertCode,
                        ["kind"] = insight.Kind.ToString()
                    }),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Month-close coach push failed for {CompetenceYm}", ym);
        }
    }

    /// <summary>Natural-language pre-close reading for the Fechar mês screen.</summary>
    public async Task<MonthClosePreviewSummaryResponse> GetClosePreviewSummaryAsync(
        string competenceYm,
        CancellationToken ct)
    {
        var ym = Competence.Require(competenceYm);
        var budget = await _budgets.GetAsync(ym, ct);
        var gaps = await _debts.ListGapsAsync(ym, ct);
        var openGaps = gaps.Where(g => !g.AlreadyRegistered).ToList();
        var previousYm = Competence.AddMonths(ym, -1);
        var previousActuals = await _budgets.GetActualsAsync(previousYm, ct);
        var summary = MonthCloseCoach.BuildPreviewSummary(
            MonthLabel(ym),
            budget.TotalPlanned,
            budget.TotalActual,
            MonthCloseCoach.FromBudget(budget),
            openGaps.Count,
            openGaps.Sum(g => g.OpenAmount),
            previousActuals,
            PtBr);
        return new MonthClosePreviewSummaryResponse(ym, summary);
    }

    /// <summary>Bell alert for a closed month, when an insight still applies.</summary>
    public async Task<AlertResponse?> BuildAlertAsync(string competenceYm, Func<string, Guid?, bool> isSeen, CancellationToken ct)
    {
        var ym = Competence.Require(competenceYm);
        var lockRow = await _repo.FirstOrDefaultAsync<MonthLock>(m => m.CompetenceYm == ym, ct, track: false);
        if (lockRow is not { IsClosed: true })
            return null;

        var budget = await _budgets.GetAsync(ym, ct);
        var previousYm = Competence.AddMonths(ym, -1);
        var previousActuals = await _budgets.GetActualsAsync(previousYm, ct);
        var insight = MonthCloseCoach.Pick(
            MonthLabel(ym),
            MonthCloseCoach.FromBudget(budget),
            previousActuals,
            PtBr);
        if (insight is null)
            return null;

        return new AlertResponse(
            MonthCloseCoach.AlertCode,
            "Attention",
            insight.Message,
            insight.CategoryId,
            Seen: isSeen(MonthCloseCoach.AlertCode, insight.CategoryId));
    }

    private static string MonthLabel(string ym)
    {
        if (!DateTime.TryParseExact(ym + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return ym;
        var name = date.ToString("MMMM/yyyy", PtBr);
        return char.ToUpper(name[0], PtBr) + name[1..];
    }
}
