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
        ["accounts"] = "Contas",
        ["cards"] = "Cartões",
        ["categories"] = "Categorias",
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
        ["accounts"] = "Contas",
        ["credit-cards"] = "Cartões",
        ["categories"] = "Categorias",
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
        ["me"] = "Conta",
        ["help"] = "Ajuda",
        ["console"] = "Console",
        ["audit-events"] = "Auditoria"
    };

    private readonly IConsoleRepository _console;

    public ConsoleService(IConsoleRepository console)
    {
        _console = console;
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
                row.Count)).ToList());
    }

    private static ConsoleMetricResponse Metric(string key, string label, int count, int baseline)
        => new(key, label, count, baseline, Percent(count, baseline));

    private static decimal Percent(int count, int baseline)
        => baseline <= 0 ? 0m : decimal.Round(count * 100m / baseline, 1, MidpointRounding.AwayFromZero);

    private static string Label(Dictionary<string, string> labels, string key)
        => labels.TryGetValue(key, out var label) ? label : key;
}
