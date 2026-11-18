using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Conora.Services;

/// <summary>Parses Meta Cloud API webhook payloads and classifies WhatsApp text intents.</summary>
public static class WhatsAppInbound
{
    public enum Kind { Text, Image, Audio }

    public enum QueryKind { None, Help, Balance, Statement, Debts, Budget }

    public sealed record Message(string From, Kind Kind, string? Text, string? MediaId);

    private static readonly string[] HelpWords =
    [
        "o que você pode", "o que voce pode", "o que pode fazer", "o que faz",
        "como funciona", "me ajuda", "preciso de ajuda", "ajuda", "comandos", "o que consigo"
    ];

    private static readonly string[] DebtWords =
        ["dívida", "divida", "dívidas", "dividas", "pendência", "pendencia", "em aberto"];

    private static readonly string[] BalanceWords =
        ["saldo", "quanto tenho", "quanto tem na conta"];

    private static readonly string[] StatementWords =
        ["extrato", "últimos lançamentos", "ultimos lancamentos", "lançamentos do mês", "lancamentos do mes"];

    private static readonly string[] BudgetWords =
    [
        "orçamento", "orcamento", "quanto sobrou", "quanto posso", "sobrou do orçamento", "sobrou do orcamento",
        "resumo", "gastar de", "gastei de", "limite de", "limite da", "limite do",
        "como tá o orçamento", "como ta o orcamento", "como está o orçamento", "como esta o orcamento",
        "como tá meu orçamento", "como ta meu orcamento", "como está meu orçamento", "como esta meu orcamento"
    ];

    private static readonly Regex CategoryHintRegex = new(
        @"(?:quanto\s+(?:posso\s+gastar|posso|sobrou)|sobrou|limite)\s+(?:ainda\s+)?(?:de|do|da|pra|para|na|no|em)\s+(?:categoria\s+)?(.+?)(?:\s+ainda)?[\s?!.]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<Message> Extract(string body)
    {
        if (!TryParse(body, out var doc))
            return [];

        using (doc)
        {
            var list = new List<Message>();
            if (!doc.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value)
                        || !value.TryGetProperty("messages", out var messages)
                        || messages.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var message in messages.EnumerateArray())
                    {
                        var from = message.TryGetProperty("from", out var f) ? f.GetString() : null;
                        if (string.IsNullOrWhiteSpace(from))
                            continue;

                        var type = message.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                        if (type == "image" && TryMediaId(message, "image", out var imageId))
                        {
                            list.Add(new Message(from, Kind.Image, null, imageId));
                            continue;
                        }

                        var audioProperty = type == "voice" ? "voice" : type == "audio" ? "audio" : null;
                        if (audioProperty is not null && TryMediaId(message, audioProperty, out var audioId))
                        {
                            list.Add(new Message(from, Kind.Audio, null, audioId));
                            continue;
                        }

                        string? text = null;
                        if (message.TryGetProperty("text", out var t) && t.TryGetProperty("body", out var b))
                            text = b.GetString();

                        if (!string.IsNullOrWhiteSpace(text))
                            list.Add(new Message(from, Kind.Text, text, null));
                    }
                }
            }

            return list;
        }
    }

    public static QueryKind Classify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return QueryKind.None;

        var trimmed = text.Trim();
        if (IsHelp(trimmed))
            return QueryKind.Help;

        var lowered = trimmed.ToLowerInvariant();
        if (lowered.Any(char.IsDigit))
            return QueryKind.None;

        if (DebtWords.Any(lowered.Contains))
            return QueryKind.Debts;
        if (BalanceWords.Any(lowered.Contains))
            return QueryKind.Balance;
        if (StatementWords.Any(lowered.Contains))
            return QueryKind.Statement;
        if (BudgetWords.Any(lowered.Contains))
            return QueryKind.Budget;

        return QueryKind.None;
    }

    public static bool IsHelp(string text)
    {
        var lowered = Fold(text.Trim());
        if (lowered is "ajuda" or "help" or "menu")
            return true;
        return HelpWords.Any(w => lowered.Contains(Fold(w), StringComparison.Ordinal));
    }

    public static bool IsQueryIntent(string text)
    {
        var kind = Classify(text);
        return kind is QueryKind.Balance or QueryKind.Statement or QueryKind.Debts or QueryKind.Budget;
    }

    public static string CategoryConfirmPrompt(string description, string categoryName)
    {
        var topic = TopicFromDescription(description);
        return string.IsNullOrWhiteSpace(topic)
            ? $"Eu achei a categoria {categoryName}. Você confirma?"
            : $"Referente a {topic}, eu achei a categoria {categoryName}. Você confirma?";
    }

    public static string TopicFromDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "";

        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gastei", "paguei", "pago", "comprei", "recebi", "ganhei", "no", "na", "de", "do", "da", "dos", "das",
            "um", "uma", "uns", "umas", "com", "para", "pra", "por", "em", "ao", "à", "o", "a", "os", "as", "reais", "r$"
        };
        var tokens = description
            .ToLowerInvariant()
            .Split([' ', '-', '/', ',', '.', ';', ':', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3 && !stop.Contains(t) && !t.Any(char.IsDigit))
            .Take(4)
            .ToList();
        return tokens.Count == 0 ? description.Trim() : string.Join(' ', tokens);
    }

    /// <summary>
    /// Pulls the category name from asks like "quanto posso gastar de iFood ainda?"
    /// or "quanto sobrou pra categoria alimentação".
    /// </summary>
    public static string? ExtractBudgetCategoryHint(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        var lowered = trimmed.ToLowerInvariant();
        if (!BudgetWords.Any(lowered.Contains) && !lowered.Contains("gastar", StringComparison.Ordinal))
            return null;

        var match = CategoryHintRegex.Match(trimmed);
        if (!match.Success)
            return null;

        var hint = match.Groups[1].Value.Trim().Trim('?', '.', '!');
        hint = Regex.Replace(hint, @"^(o|a|os|as|categoria)\s+", "", RegexOptions.IgnoreCase).Trim();
        var noise = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "orçamento", "orcamento", "mês", "mes", "esse mês", "este mês", "do mês", "do mes", "ainda", "resumo"
        };
        if (string.IsNullOrWhiteSpace(hint) || noise.Contains(hint))
            return null;

        return hint;
    }

    public static string FormatCategoryBudgetReply(
        string monthLabel,
        string categoryName,
        decimal planned,
        decimal actual,
        CultureInfo culture)
    {
        var remaining = planned - actual;
        if (planned <= 0)
        {
            return $"Em {monthLabel}, a categoria {categoryName} ainda não tem valor planejado no orçamento. " +
                   "No app, em Orçamento, você define o limite do mês.";
        }

        var spent = $"planejado R$ {planned.ToString("N2", culture)}, gasto R$ {actual.ToString("N2", culture)}";
        if (remaining >= 0)
        {
            return $"Em {categoryName} ({monthLabel}): {spent}. Você ainda pode gastar R$ {remaining.ToString("N2", culture)}.";
        }

        return $"Em {categoryName} ({monthLabel}): {spent}. Já passou R$ {Math.Abs(remaining).ToString("N2", culture)} do planejado.";
    }

    /// <summary>
    /// Global month budget snapshot for WhatsApp (planned vs actual, net income, open debts, spendable left).
    /// </summary>
    public static string FormatMonthBudgetReply(
        string monthLabel,
        decimal totalPlanned,
        decimal totalActual,
        decimal netIncome,
        decimal openDebtsTotal,
        int openDebtCount,
        CultureInfo culture)
    {
        var money = (decimal value) => MoneyRounded(value, culture);
        var remainingBudget = totalPlanned - totalActual;
        var available = netIncome - openDebtsTotal;

        var parts = new List<string>();
        if (totalPlanned <= 0)
        {
            parts.Add($"Em {monthLabel} ainda não tem orçamento planejado. No app, em Orçamento, você monta o mês.");
        }
        else if (remainingBudget >= 0)
        {
            parts.Add(
                $"Em {monthLabel}, dos {money(totalPlanned)} orçados, {money(totalActual)} já foram realizados — " +
                $"sobram {money(remainingBudget)} no planejado.");
        }
        else
        {
            parts.Add(
                $"Em {monthLabel}, dos {money(totalPlanned)} orçados, {money(totalActual)} já foram realizados — " +
                $"passou {money(Math.Abs(remainingBudget))} do planejado.");
        }

        parts.Add($"Receita líquida do mês: {money(netIncome)}.");

        if (openDebtCount <= 0 || openDebtsTotal <= 0)
            parts.Add("Contas e dívidas em aberto: nada pendente agora.");
        else if (openDebtCount == 1)
            parts.Add($"Ainda tem {money(openDebtsTotal)} a pagar em 1 conta/dívida em aberto.");
        else
            parts.Add($"Ainda tem {money(openDebtsTotal)} a pagar em {openDebtCount} contas/dívidas em aberto.");

        if (available >= 0)
            parts.Add($"Líquido disponível pra gastar: {money(available)} (receita líquida menos o que ainda tem que sair).");
        else
            parts.Add(
                $"Líquido disponível pra gastar: negativo em {money(Math.Abs(available))} — " +
                "o que ainda tem que sair supera a receita líquida.");

        return string.Join(' ', parts);
    }

    private static string MoneyRounded(decimal value, CultureInfo culture)
        => $"R$ {Math.Round(value, MidpointRounding.AwayFromZero).ToString("N0", culture)}";

    private static bool TryMediaId(JsonElement message, string propertyName, out string mediaId)
    {
        mediaId = "";
        if (!message.TryGetProperty(propertyName, out var media)
            || !media.TryGetProperty("id", out var idEl))
            return false;
        var raw = idEl.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        mediaId = raw;
        return true;
    }

    private static bool TryParse(string body, out JsonDocument doc)
    {
        try
        {
            doc = JsonDocument.Parse(body);
            return true;
        }
        catch (JsonException)
        {
            doc = null!;
            return false;
        }
    }

    private static string Fold(string value)
        => value.ToLowerInvariant()
            .Replace("á", "a").Replace("à", "a").Replace("ã", "a").Replace("â", "a")
            .Replace("é", "e").Replace("ê", "e")
            .Replace("í", "i")
            .Replace("ó", "o").Replace("ô", "o").Replace("õ", "o")
            .Replace("ú", "u").Replace("ç", "c");
}
