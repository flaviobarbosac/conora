using System.Globalization;
using System.Text;
using Conora.Domain.Enums;
using Conora.Domain.Services;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed partial class WhatsAppService
{
    private async Task<string> AnswerQueryAsync(string text, CancellationToken ct)
        => WhatsAppInbound.Classify(text) switch
        {
            WhatsAppInbound.QueryKind.Debts => await AnswerDebtsAsync(ct),
            WhatsAppInbound.QueryKind.Balance => await AnswerBalanceAsync(ct),
            WhatsAppInbound.QueryKind.Statement => await AnswerStatementAsync(ct),
            _ => await AnswerBudgetRemainingAsync(text, ct)
        };

    private static string BuildHelpReply()
        => """
           Eu posso te ajudar por aqui assim:
           • Lançar despesa ou receita por texto, áudio ou foto de cupom
           • Conversar com você até confirmar valor, categoria e conta — só gravo depois do seu sim
           • Consultar o saldo das contas
           • Mostrar um extrato resumido do mês
           • Listar dívidas em aberto
           • Ver o orçamento do mês (orçado x realizado, receita líquida, dívidas e quanto sobra) ou o limite de uma categoria (ex.: quanto posso gastar de iFood ainda)
           • Parear este WhatsApp com o app (no Conora: Configurações → WhatsApp)

           Manda no seu ritmo. Se quiser cancelar um lançamento no meio, diga cancelar.
           """;

    private async Task<string> AnswerBalanceAsync(CancellationToken ct)
    {
        var accounts = await _accounts.ListAsync(includeArchived: false, ct);
        if (accounts.Count == 0)
            return "Ainda não achei conta bancária cadastrada. No app, em Contas, você cadastra e eu passo a mostrar o saldo por aqui.";

        var total = accounts.Sum(a => a.Balance);
        var sb = new StringBuilder();
        sb.AppendLine($"Saldo nas contas: R$ {total.ToString("N2", PtBr)}.");
        foreach (var account in accounts.Take(8))
            sb.AppendLine($"• {account.Name}: R$ {account.Balance.ToString("N2", PtBr)}");
        if (accounts.Count > 8)
            sb.Append($"… e mais {accounts.Count - 8} conta(s) no app.");
        return sb.ToString().TrimEnd();
    }

    private async Task<string> AnswerStatementAsync(CancellationToken ct)
    {
        var ym = Competence.From(DateTime.UtcNow);
        var page = await _entries.SearchAsync(new EntryFilter(CompetenceYm: ym, Take: 8), ct);
        if (page.Items.Count == 0)
            return $"No extrato de {MonthLabel(ym)} ainda não tem lançamento. Quando quiser, manda um gasto por texto, áudio ou foto.";

        var sb = new StringBuilder();
        sb.AppendLine($"Extrato resumido de {MonthLabel(ym)} (últimos {page.Items.Count}):");
        foreach (var entry in page.Items)
        {
            var sign = entry.Type is EntryType.Income ? "+" : "-";
            var when = entry.OccurredAt.ToString("dd/MM", PtBr);
            sb.AppendLine($"• {when} {sign}R$ {entry.Amount.ToString("N2", PtBr)} — {entry.Description}");
        }

        if (page.TotalCount > page.Items.Count)
            sb.Append("No app você vê o extrato completo.");
        return sb.ToString().TrimEnd();
    }

    private async Task<string> AnswerDebtsAsync(CancellationToken ct)
    {
        var debts = await _debts.ListOpenAsync(ct);
        if (debts.Count == 0)
            return "Boa notícia: não tem dívida em aberto no momento.";

        var total = debts.Sum(d => d.OpenAmount);
        var sb = new StringBuilder();
        sb.AppendLine($"Dívidas em aberto: {debts.Count}, total R$ {total.ToString("N2", PtBr)}.");
        foreach (var debt in debts.Take(10))
        {
            sb.AppendLine(
                $"• {debt.CategoryName} ({debt.CompetenceYm}): aberto R$ {debt.OpenAmount.ToString("N2", PtBr)}");
        }

        if (debts.Count > 10)
            sb.Append($"… e mais {debts.Count - 10} no app.");
        return sb.ToString().TrimEnd();
    }

    private async Task<string> AnswerBudgetRemainingAsync(string text, CancellationToken ct)
    {
        var ym = Competence.From(DateTime.UtcNow);
        var budget = await _budgets.GetAsync(ym, ct);
        var label = MonthLabel(ym);

        var hint = WhatsAppInbound.ExtractBudgetCategoryHint(text);
        if (!string.IsNullOrWhiteSpace(hint))
        {
            var line = FindBudgetLine(budget, hint);
            if (line is null)
            {
                return $"Não achei a categoria \"{hint}\" no orçamento de {label}. " +
                       "Confere o nome no app, em Orçamento, ou pergunta de novo com o nome certinho.";
            }

            return WhatsAppInbound.FormatCategoryBudgetReply(
                label,
                line.CategoryName,
                line.PlannedAmount,
                line.ActualAmount,
                PtBr);
        }

        var debts = await _debts.ListOpenAsync(ct);
        var openTotal = debts.Sum(d => d.OpenAmount);
        var netIncome = ResolveNetIncome(budget);

        return WhatsAppInbound.FormatMonthBudgetReply(
            label,
            budget.TotalPlanned,
            budget.TotalActual,
            netIncome,
            openTotal,
            debts.Count,
            PtBr);
    }

    /// <summary>
    /// Prefer received income net of discount section when available; otherwise diagnosis spendable.
    /// </summary>
    private static decimal ResolveNetIncome(BudgetResponse budget)
    {
        var discountActual = budget.Sections
            .FirstOrDefault(s => s.Section == CategorySection.Discount)?.ActualAmount ?? 0m;
        var fromReceived = Math.Max(0m, budget.ReceivedIncome - discountActual);
        if (fromReceived > 0)
            return fromReceived;

        var discountPlanned = budget.Sections
            .FirstOrDefault(s => s.Section == CategorySection.Discount)?.PlannedAmount ?? 0m;
        if (budget.ReceivedIncome > 0)
            return Math.Max(0m, budget.ReceivedIncome - discountPlanned);

        return budget.SpendableIncome > 0 ? budget.SpendableIncome : 0m;
    }

    private static BudgetLineResponse? FindBudgetLine(BudgetResponse budget, string hint)
    {
        var foldedHint = hint.Trim();
        var analytical = budget.Lines.Where(l => !l.IsGroup).ToList();
        var pool = analytical.Count > 0 ? analytical : budget.Lines.ToList();

        var exact = pool.FirstOrDefault(l =>
            string.Equals(l.CategoryName, foldedHint, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        var contains = pool
            .Select(l => (line: l, score: CategoryNameScore(l.CategoryName, foldedHint)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.line.CategoryName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.line)
            .FirstOrDefault();
        return contains;
    }

    private static int CategoryNameScore(string categoryName, string hint)
    {
        var name = categoryName.ToLowerInvariant();
        var needle = hint.ToLowerInvariant();
        if (name == needle)
            return 100;
        if (name.Contains(needle, StringComparison.Ordinal) || needle.Contains(name, StringComparison.Ordinal))
            return 50 + Math.Min(needle.Length, 20);
        var tokens = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Count(t => t.Length >= 3 && name.Contains(t, StringComparison.Ordinal)) * 10;
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
