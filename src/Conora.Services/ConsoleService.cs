using Conora.Repository;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class ConsoleService
{
    private static readonly Dictionary<string, string> FeatureLabels = new(StringComparer.Ordinal)
    {
        ["entries"] = "Lançamentos",
        ["budgets"] = "Orçamento",
        ["months"] = "Fechamento",
        ["diagnosis"] = "Diagnóstico",
        ["accounts"] = "Conta bancária",
        ["cards"] = "Cartões",
        ["categories"] = "Plano de contas",
        ["projects"] = "Projetos de vida",
        ["patrimony"] = "Patrimônio",
        ["imports"] = "Importação",
        ["receipts"] = "Recibos",
        ["whatsapp"] = "WhatsApp",
        ["family"] = "Família",
        ["members"] = "Membros",
        ["ian"] = "Ian",
        ["feedback"] = "Feedback"
    };

    private static readonly Dictionary<string, string> AreaLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["entries"] = "Lançamentos",
        ["budgets"] = "Orçamento",
        ["months"] = "Fechamento",
        ["diagnosis"] = "Diagnóstico",
        ["accounts"] = "Conta bancária",
        ["credit-cards"] = "Cartões",
        ["categories"] = "Plano de contas",
        ["life-projects"] = "Projetos de vida",
        ["patrimony"] = "Patrimônio",
        ["imports"] = "Importação",
        ["receipts"] = "Recibos",
        ["whatsapp"] = "WhatsApp",
        ["webhooks"] = "Webhooks",
        ["family"] = "Família",
        ["members"] = "Membros",
        ["ai"] = "Ian",
        ["dashboard"] = "Início",
        ["export"] = "Exportação",
        ["auth"] = "Acesso",
        ["plan"] = "Plano",
        ["feedback"] = "Feedback",
        ["gamification"] = "Jornada",
        ["users"] = "Usuários",
        ["me"] = "Conta do usuário",
        ["help"] = "Ajuda",
        ["console"] = "Console",
        ["audit-events"] = "Auditoria"
    };

    private readonly IConsoleRepository _console;

    public ConsoleService(IConsoleRepository console)
    {
        _console = console;
    }

    public async Task<IReadOnlyList<JourneyAbandonmentRow>> ListAbandonmentAsync(CancellationToken ct)
    {
        var rows = await _console.ListJourneyAbandonmentAsync(ct);
        return rows.Select(r => new JourneyAbandonmentRow(
            r.UsuarioId,
            r.Email,
            r.Name,
            r.FirstLoginAt,
            r.LastSeenAt,
            r.FirstPath,
            r.LastPath,
            r.HasEntry,
            r.OnboardingCompleted,
            r.PathTrail)).ToList();
    }

    public async Task<ConsoleKpisResponse> GetAsync(CancellationToken ct)
    {
        var data = await _console.ReadAsync(ct);
        var accounts = data.Accounts;
        var withoutBudget = Math.Max(0, accounts - data.WithBudgetThisMonth);
        var budgetBase = data.WithBudgetThisMonth;
        var previousOpen = Math.Max(0, data.WithEntryPrevious - data.ClosedPrevious);
        var outsidePrevious = Math.Max(0, data.ClosedPrevious - data.WithinPrevious);

        return new ConsoleKpisResponse(
            data.GeneratedAtUtc,
            data.CompetenceYm,
            data.PreviousCompetenceYm,
            accounts,
            data.Active1d,
            data.Active7d,
            data.Active30d,
            data.Errors7d,
            data.Errors30d,
            data.WithBudgetThisMonth,
            withoutBudget,
            data.BudgetWithin,
            data.BudgetOver,
            data.BudgetEmpty,
            [
                Metric("onboarding", "Onboarding concluído", data.OnboardingCompleted, accounts),
                Metric("diagnosis", "Diagnóstico de renda", data.WithDiagnosis, accounts),
                Metric("budget", "Já fez orçamento", data.WithBudgetEver, accounts),
                Metric("card", "Cartão cadastrado", data.WithCard, accounts),
                Metric("close", "Já fechou um mês", data.WithCloseEver, accounts),
                Metric("reserve", "Reserva com saldo", data.WithReserve, accounts),
                Metric("projects", "Projeto de vida", data.WithProject, accounts)
            ],
            [
                Metric("within-ever", "Fechou dentro do teto", data.WithinEver, accounts),
                Metric("streak", "3 meses seguidos no teto", data.ThreeMonthStreak, accounts),
                Metric("reserve-complete", "Reserva cobre a meta", data.ReserveComplete, accounts),
                Metric("project-advanced", "Projeto com aporte", data.ProjectAdvanced, accounts)
            ],
            [
                Metric("previous-entries", "Com lançamento", data.WithEntryPrevious, accounts),
                Metric("previous-closed", "Mês fechado", data.ClosedPrevious, data.WithEntryPrevious),
                Metric("previous-open", "Mês ainda aberto", previousOpen, data.WithEntryPrevious),
                Metric("previous-within", "Fechado dentro do teto", data.WithinPrevious, data.ClosedPrevious),
                Metric("previous-over", "Fechado acima do teto", outsidePrevious, data.ClosedPrevious)
            ],
            data.Features.Select(feature => new ConsoleFeatureResponse(
                feature.Key,
                Label(FeatureLabels, feature.Key),
                feature.Users,
                feature.Events,
                Percent(feature.Users, accounts))).ToList(),
            data.Weeks.Select(week => new ConsoleWeekResponse(
                week.WeekStart.ToString("yyyy-MM-dd"),
                week.Signups,
                week.Active,
                week.Budgets,
                week.Closes,
                week.Errors)).ToList(),
            data.ErrorDays.Select(day => Metric(day.Day.ToString("yyyy-MM-dd"), day.Day.ToString("dd/MM"), day.Count, data.Errors30d)).ToList(),
            data.ErrorsByStatus.Select(row => Metric(row.StatusCode.ToString(), $"{row.StatusCode} {row.Title}", row.Count, data.Errors30d)).ToList(),
            data.ErrorsByArea.Select(row => Metric(row.Area, Label(AreaLabels, row.Area), row.Count, data.Errors30d)).ToList(),
            data.ErrorRows.Select(row => new ConsoleErrorRowResponse(
                row.Day.ToString("yyyy-MM-dd"),
                row.StatusCode,
                row.Title,
                Label(AreaLabels, row.Area),
                row.Count)).ToList(),
            data.Months.Select(month =>
            {
                var without = Math.Max(0, month.Base - month.WithBudget);
                var closedOver = Math.Max(0, month.Closed - month.ClosedWithin);
                return new ConsoleMonthResponse(
                    month.CompetenceYm,
                    month.Base,
                    month.WithBudget,
                    without,
                    month.BudgetWithin,
                    month.BudgetOver,
                    month.BudgetEmpty,
                    month.WithEntry,
                    month.Closed,
                    month.ClosedWithin,
                    closedOver,
                    month.Signups,
                    month.Errors,
                    Percent(month.WithBudget, month.Base),
                    Percent(month.BudgetWithin, month.WithBudget),
                    Percent(month.Closed, month.WithEntry));
            }).ToList());
    }

    private static ConsoleMetricResponse Metric(string key, string label, int count, int baseline)
        => new(key, label, count, baseline, Percent(count, baseline));

    private static decimal Percent(int count, int baseline)
        => baseline <= 0 ? 0m : decimal.Round(count * 100m / baseline, 1, MidpointRounding.AwayFromZero);

    private static string Label(Dictionary<string, string> labels, string key)
        => labels.TryGetValue(key, out var label) ? label : key;
}
