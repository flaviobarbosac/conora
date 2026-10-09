using System.Globalization;
using System.Text;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class DashboardService
{
    private sealed record Totals(
        decimal Income, decimal Received, decimal Expense, decimal Contributions, decimal CardPurchases, decimal ProjectContributions);

    private readonly IFinanceRepository _repo;
    private readonly MonthService _months;
    private readonly BudgetService _budgets;
    private readonly ChartAccountService _chartAccounts;
    private readonly FamilyGroupService _family;

    public DashboardService(
        IFinanceRepository repo,
        MonthService months,
        BudgetService budgets,
        ChartAccountService chartAccounts,
        FamilyGroupService family)
    {
        _repo = repo;
        _months = months;
        _budgets = budgets;
        _chartAccounts = chartAccounts;
        _family = family;
    }

    public async Task<DashboardResponse> GetAsync(string competenceYm, CancellationToken ct)
    {
        var ym = Competence.Require(competenceYm);
        var totals = await ComputeTotalsAsync(ym, ct);
        var month = await _months.GetAsync(ym, ct);
        var accounts = await _repo.ListAsync<Account>(a => !a.IsArchived, ct);
        var alerts = await BuildAlertsAsync(ym, totals, ct);

        return new DashboardResponse(
            ym,
            month.IsClosed,
            totals.Income,
            totals.Received,
            totals.Expense,
            totals.Income - totals.Expense,
            totals.Contributions,
            totals.CardPurchases,
            totals.ProjectContributions,
            accounts.Sum(a => a.Balance),
            alerts);
    }

    public Task<IReadOnlyList<AlertResponse>> GetAlertsAsync(string competenceYm, CancellationToken ct)
        => GetAlertsCoreAsync(Competence.Require(competenceYm), ct);

    public async Task<MonthlyReportResponse> GetReportAsync(string competenceYm, CancellationToken ct)
    {
        var ym = Competence.Require(competenceYm);
        var summary = await GetAsync(ym, ct);

        var actuals = await _budgets.GetActualsAsync(ym, ct);
        var names = (await _chartAccounts.ListAsync(null, true, true, ct)).ToDictionary(c => c.Id, c => c.Name);
        var ByAccount = actuals
            .Select(kv => new ChartAccountTotalResponse(kv.Key, names.GetValueOrDefault(kv.Key, "—"), kv.Value))
            .OrderByDescending(c => c.Amount)
            .ToList();

        var previousYm = Competence.AddMonths(ym, -1);
        var previous = await ComputeTotalsAsync(previousYm, ct);
        return new MonthlyReportResponse(
            summary,
            ByAccount,
            previousYm,
            previous.Expense,
            summary.ExpenseTotal - previous.Expense,
            previous.Income - previous.Expense);
    }

    /// <summary>CSV of the month's movement (all entries when no competence is given). Read-only, allowed in read-only plans.</summary>
    public async Task<ExportFile> ExportEntriesAsync(string? competenceYm, CancellationToken ct)
    {
        var ym = string.IsNullOrWhiteSpace(competenceYm) ? null : Competence.Require(competenceYm);
        var entries = await _repo.ListAsync<Entry>(e => ym == null || e.CompetenceYm == ym, ct);
        var categories = (await _chartAccounts.ListAsync(null, true, true, ct)).ToDictionary(c => c.Id, c => c.Name);
        var accounts = (await _repo.ListAsync<Account>(null, ct)).ToDictionary(a => a.Id, a => a.Name);
        var members = (await _repo.ListAsync<FamilyMember>(null, ct)).ToDictionary(m => m.Id, m => m.Name);

        var sb = new StringBuilder();
        sb.AppendLine("Data;Competencia;Tipo;Valor;Conta;ContaDestino;conta;Membro;Descricao");
        foreach (var e in entries.OrderBy(e => e.OccurredAt))
        {
            sb.AppendLine(string.Join(';',
                e.OccurredAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                e.CompetenceYm,
                e.Type,
                e.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                Cell(e.AccountId is Guid a ? accounts.GetValueOrDefault(a) : null),
                Cell(e.ContraAccountId is Guid c ? accounts.GetValueOrDefault(c) : null),
                Cell(e.ChartAccountId is Guid k ? categories.GetValueOrDefault(k) : null),
                Cell(e.MemberId is Guid m ? members.GetValueOrDefault(m) : null),
                Cell(e.Description)));
        }

        return ToFile($"movimento-{ym ?? "completo"}.csv", sb.ToString());
    }

    public async Task<ExportFile> ExportSummaryAsync(string competenceYm, CancellationToken ct)
    {
        var ym = Competence.Require(competenceYm);
        var report = await GetReportAsync(ym, ct);
        var s = report.Summary;

        var sb = new StringBuilder();
        sb.AppendLine("Item;Valor");
        void Line(string label, decimal value) => sb.AppendLine($"{label};{value.ToString("0.00", CultureInfo.InvariantCulture)}");
        Line("Receita", s.IncomeTotal);
        Line("Despesa", s.ExpenseTotal);
        Line("Resultado", s.Result);
        Line("Contribuicoes/Doacoes", s.ContributionsTotal);
        Line("Compras no cartao", s.CardPurchasesTotal);
        Line("Aportes em projetos", s.ProjectContributionsTotal);
        sb.AppendLine();
        sb.AppendLine("conta;Valor");
        foreach (var c in report.ByAccount)
            sb.AppendLine($"{Cell(c.ChartAccountName)};{c.Amount.ToString("0.00", CultureInfo.InvariantCulture)}");

        return ToFile($"resumo-{ym}.csv", sb.ToString());
    }

    private async Task<IReadOnlyList<AlertResponse>> GetAlertsCoreAsync(string ym, CancellationToken ct)
    {
        var totals = await ComputeTotalsAsync(ym, ct);
        return await BuildAlertsAsync(ym, totals, ct);
    }

    /// <summary>
    /// Income = diagnosis net spendable (once per source) + income entries not tied to a diagnosed source.
    /// Entries linked to a source in the same competence only confirm cash and are never summed again.
    /// Transfers, card payments and project contributions never touch the monthly result.
    /// </summary>
    private async Task<Totals> ComputeTotalsAsync(string ym, CancellationToken ct)
    {
        var userIds = await _family.GetReadableUsuarioIdsAsync(ct);
        var multi = userIds.Count > 1;

        var sources = multi
            ? await _repo.ListAnyTenantAsync<IncomeSource>(s => userIds.Contains(s.UsuarioId) && s.CompetenceYm == ym, ct)
            : await _repo.ListAsync<IncomeSource>(s => s.CompetenceYm == ym, ct);
        var sourceIds = sources.Select(s => s.Id).ToHashSet();
        var incomes = multi
            ? await _repo.ListAnyTenantAsync<Entry>(
                e => userIds.Contains(e.UsuarioId) && e.CompetenceYm == ym && e.Type == EntryType.Income, ct)
            : await _repo.ListAsync<Entry>(e => e.CompetenceYm == ym && e.Type == EntryType.Income, ct);

        var extra = incomes.Where(e => e.IncomeSourceId is null || !sourceIds.Contains(e.IncomeSourceId.Value)).Sum(e => e.Amount);
        var income = sources.Sum(s => s.NetSpendable) + extra;

        decimal expenses;
        decimal contributions;
        decimal cardPurchases;
        decimal projects;
        if (multi)
        {
            expenses = (await _repo.ListAnyTenantAsync<Entry>(
                e => userIds.Contains(e.UsuarioId) && e.CompetenceYm == ym && e.Type == EntryType.Expense, ct)).Sum(e => e.Amount);
            contributions = (await _repo.ListAnyTenantAsync<Entry>(
                e => userIds.Contains(e.UsuarioId) && e.CompetenceYm == ym && e.Type == EntryType.Contribution, ct)).Sum(e => e.Amount);
            cardPurchases = (await _repo.ListAnyTenantAsync<CardPurchase>(
                p => userIds.Contains(p.UsuarioId) && p.CompetenceYm == ym, ct)).Sum(p => p.Amount);
            projects = (await _repo.ListAnyTenantAsync<Entry>(
                e => userIds.Contains(e.UsuarioId) && e.CompetenceYm == ym && e.Type == EntryType.ProjectContribution, ct)).Sum(e => e.Amount);
        }
        else
        {
            expenses = await _repo.SumAsync<Entry>(e => e.CompetenceYm == ym && e.Type == EntryType.Expense, e => e.Amount, ct);
            contributions = await _repo.SumAsync<Entry>(e => e.CompetenceYm == ym && e.Type == EntryType.Contribution, e => e.Amount, ct);
            cardPurchases = await _repo.SumAsync<CardPurchase>(p => p.CompetenceYm == ym, p => p.Amount, ct);
            projects = await _repo.SumAsync<Entry>(e => e.CompetenceYm == ym && e.Type == EntryType.ProjectContribution, e => e.Amount, ct);
        }

        return new Totals(income, incomes.Sum(e => e.Amount), expenses + contributions + cardPurchases, contributions, cardPurchases, projects);
    }

    private async Task<IReadOnlyList<AlertResponse>> BuildAlertsAsync(string ym, Totals totals, CancellationToken ct)
    {
        var alerts = new List<AlertResponse>();

        var budget = await _budgets.GetAsync(ym, ct);
        foreach (var line in budget.Lines.Where(l => l.Status != "Ok"))
        {
            alerts.Add(new AlertResponse(
                $"BUDGET_{line.Status.ToUpperInvariant()}",
                line.Status,
                MessageFor(line.Status, $"conta {line.ChartAccountName}", line.Percent),
                line.ChartAccountId,
                line.Percent));
        }

        var cards = await _repo.ListAsync<CreditCard>(null, ct);
        var unpaid = await _repo.ListAsync<CardInvoice>(i => i.Status != InvoiceStatus.Paid, ct);
        foreach (var card in cards.Where(c => c.LimitTotal > 0))
        {
            var used = unpaid.Where(i => i.CreditCardId == card.Id).Sum(i => i.Total);
            var (percent, status) = BudgetService.Evaluate(card.LimitTotal, used);
            if (status != "Ok")
                alerts.Add(new AlertResponse("CARD_LIMIT", status, MessageFor(status, $"Limite do cartão {card.Name}", percent), null, percent));
        }

        if (totals.Income - totals.Expense < 0)
            alerts.Add(new AlertResponse("NEGATIVE_RESULT", "Attention", "As despesas do mês superam a receita."));

        return alerts;
    }

    private static string MessageFor(string status, string subject, decimal? percent) => status switch
    {
        "Attention" => $"{subject}: {percent:0.#}% usado. Atenção.",
        "Limit" => $"{subject}: 100% usado. Limite atingido.",
        _ => percent is null ? $"{subject}: gasto sem valor planejado." : $"{subject}: {percent:0.#}% usado. Limite ultrapassado."
    };

    /// <summary>Neutralizes spreadsheet formula injection and CSV delimiters in free text.</summary>
    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var text = value.Replace('\r', ' ').Replace('\n', ' ');
        if ("=+-@\t".Contains(text[0]))
            text = "'" + text;

        return text.Contains(';') || text.Contains('"') ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }

    private static ExportFile ToFile(string name, string csv)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(csv);
        return new ExportFile(name, "text/csv; charset=utf-8", preamble.Concat(body).ToArray());
    }
}
