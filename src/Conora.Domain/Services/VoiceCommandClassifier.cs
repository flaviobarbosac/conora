using System.Globalization;
using System.Text.RegularExpressions;

namespace Conora.Domain.Services;

/// <summary>
/// Local reading of a spoken command when Gemini is off or returns nothing usable.
/// The API never navigates or saves; it only classifies.
/// </summary>
public static partial class VoiceCommandClassifier
{
    public const string Navigate = "navigate";
    public const string Action = "action";
    public const string Ask = "ask";
    public const string CreateEntry = "create-entry";
    public const string CreateBudget = "create-budget";

    public sealed record Result(
        string Intent,
        string? Route,
        string? Action,
        Entry? Entry,
        string Reply);

    public sealed record Entry(
        string Type,
        decimal? Amount,
        string? Description,
        string? CategoryName,
        string? AccountName,
        string? Date = null);

    private static readonly (string Route, string[] Keys)[] Screens =
    [
        ("/orcamento", ["orcamento"]),
        ("/raio-x", ["raio-x", "raio x", "raiox"]),
        ("/lancamentos", ["lancamentos"]),
        ("/categorias", ["categorias", "plano de contas", "plano de conta"]),
        ("/cartoes", ["cartoes", "cartao"]),
        ("/contas", ["contas bancarias", "conta bancaria", "contas"]),
        ("/projetos", ["projetos de vida", "projetos"]),
        ("/patrimonio", ["patrimonio"]),
        ("/membros", ["membros", "familia"]),
        ("/marcos", ["marcos"]),
        ("/relatorios", ["relatorios"]),
        ("/diagnostico", ["diagnostico"]),
        ("/importar", ["importar", "ofx"]),
        ("/mes", ["fechar o mes", "fechar mes", "fechamento"]),
        ("/perfil", ["perfil"]),
        ("/configuracoes", ["configuracoes", "configuracao"]),
        ("/ajuda", ["ajuda"]),
        ("/plano", ["meu plano", "assinatura"]),
        ("/whatsapp", ["whatsapp"]),
        ("/", ["inicio", "tela inicial", "home"]),
    ];

    public static Result Classify(string transcript)
    {
        var raw = transcript?.Trim() ?? string.Empty;
        var text = IanSubjectMatcher.Normalize(raw);
        if (text.Length == 0)
            return new Result(Ask, null, null, null, "Não ouvi nada. Fale de novo.");

        var opens = OpensScreen(text);
        var acts = AsksToPost(text);
        var amount = ReadAmount(text);
        var screen = MatchScreen(text);

        if (opens && screen is not null && !acts)
            return new Result(Navigate, screen, null, null, $"Abrindo {Label(screen)}.");

        if (AsksForBudget(text, amount))
        {
            var budget = ReadEntry(raw, text, amount);
            return new Result(Action, null, CreateBudget, budget, "Entendi o orçamento.");
        }

        if (acts || text.Contains("aporte") || (amount is not null && (text.Contains("despesa") || text.Contains("receita") || text.Contains("reais") || text.Contains("quinhentos"))))
        {
            var entry = ReadEntry(raw, text, amount);
            return new Result(Action, null, CreateEntry, entry, "Entendi o lançamento.");
        }

        if (opens && screen is not null)
            return new Result(Navigate, screen, null, null, $"Abrindo {Label(screen)}.");

        if (IanInsight.LooksLike(text))
            return new Result(Ask, null, null, null, "");

        if (LooksLikeHelp(text))
            return new Result(Ask, null, null, null, HelpReply);

        if (LooksLikeOpen(text))
            return new Result(Ask, null, null, null, "");

        if (AsksForReport(text))
            return new Result(Ask, null, null, null, ReportPausedReply);

        return new Result(Ask, null, null, null, string.Empty);
    }

    public const string ScopeReply = "Posso abrir uma tela, lançar uma despesa ou uma receita, ou registrar um orçamento.";

    public const string HelpReply =
        "Posso abrir uma tela, lançar uma despesa ou uma receita, registrar um orçamento e dizer para onde a sua receita está indo, neste mês ou nos últimos meses, inclusive em essencial, social, descontos ou projetos.";

    public static bool LooksLikeOpen(string? transcript)
    {
        if (LooksLikeGreeting(transcript))
            return true;

        var text = IanSubjectMatcher.Normalize(transcript);
        if (text.Length == 0 || AsksToPost(text) || OpensScreen(text) || ReadAmount(text) is not null)
            return false;
        if (text.Contains("vaz") || text.Contains("detalh") || text.Contains("aprofund") || text.Contains("ultim"))
            return false;

        return text.Contains("como estamos")
            || text.Contains("como esta hoje")
            || text.Contains("como vamos");
    }

    public static string OpenFallback(DateTime local)
    {
        var hello = local.Hour < 12 ? "Bom dia" : local.Hour < 18 ? "Boa tarde" : "Boa noite";
        return $"{hello}! Em que posso ajudar?";
    }

    public static bool LooksLikeGreeting(string? transcript)
    {
        var text = IanSubjectMatcher.Normalize(transcript);
        if (text.Length == 0 || AsksToPost(text) || OpensScreen(text) || ReadAmount(text) is not null)
            return false;

        return text is "oi" or "ola" or "bom dia" or "boa tarde" or "boa noite" or "boa madrugada"
                or "e ai" or "tudo bem" or "como vai"
            || text.StartsWith("bom dia ", StringComparison.Ordinal)
            || text.StartsWith("boa tarde ", StringComparison.Ordinal)
            || text.StartsWith("boa noite ", StringComparison.Ordinal)
            || text.StartsWith("oi ", StringComparison.Ordinal)
            || text.StartsWith("ola ", StringComparison.Ordinal);
    }

    public static bool LooksLikeHelp(string? transcript)
    {
        var text = IanSubjectMatcher.Normalize(transcript);
        if (text.Length == 0 || AsksToPost(text) || OpensScreen(text))
            return false;

        return text.Contains("pode ajudar")
            || text.Contains("me ajudar")
            || text.Contains("voce ajuda")
            || text.Contains("em que voce")
            || text.Contains("no que voce")
            || text.Contains("o que voce pode")
            || text.Contains("o que voce faz")
            || text.Contains("o que voce sabe")
            || text.Contains("como voce pode")
            || text.Contains("como pode ajudar")
            || text.Contains("suas funcoes")
            || text.Contains("que voce faz");
    }

    public const string ReportPausedReply = ScopeReply;

    public static bool LooksLikeReport(string? transcript)
        => AsksForReport(IanSubjectMatcher.Normalize(transcript));

    public static bool IsAllowedRoute(string? route)
        => route is not null && Screens.Any(screen => screen.Route == route);

    private static bool OpensScreen(string text)
        => HasWord(text, "abre") || HasWord(text, "abrir") || HasWord(text, "abra")
           || HasWord(text, "mostra") || HasWord(text, "mostrar")
           || HasWord(text, "mover") || HasWord(text, "mova")
           || HasWord(text, "leva") || HasWord(text, "leve") || HasWord(text, "levar")
           || text.Contains("vai para") || text.Contains("vai pra")
           || text.Contains("ir para") || text.Contains("ir pra")
           || text.Contains("va para") || text.Contains("va pra")
           || text.Contains("tela de") || text.Contains("tela do")
           || text.Contains("pra tela") || text.Contains("para a tela") || text.Contains("para tela");

    private static bool AsksForReport(string text)
        => text.Contains("relatorio")
           || text.Contains("como esta meu mes")
           || text.Contains("como esta o mes")
           || text.Contains("como esta o orcamento")
           || text.Contains("resumo do mes")
           || text.Contains("resumo do orcamento")
           || text.Contains("evoluc")
           || text.Contains("onde gastei")
           || text.Contains("composic")
           || (text.Contains("mes") && (text.Contains("despesa") || text.Contains("gasto")) && !AsksToPost(text));

    private static bool AsksForBudget(string text, decimal? amount)
        => text.Contains("orcamento")
           && !text.Contains("como esta")
           && (AsksToPost(text) || text.Contains("registro") || text.Contains("categoria") || amount is not null);

    private static bool AsksToPost(string text)
        => HasWord(text, "lanca") || HasWord(text, "lancar") || HasWord(text, "lancamento")
           || HasWord(text, "registra") || HasWord(text, "registrar") || text.Contains("faz um");

    private static bool HasWord(string text, string word)
        => text == word || text.StartsWith(word + " ", StringComparison.Ordinal) || text.EndsWith(" " + word, StringComparison.Ordinal) || text.Contains(" " + word + " ", StringComparison.Ordinal);

    private static string? MatchScreen(string text)
    {
        foreach (var (route, keys) in Screens)
        {
            if (keys.Any(text.Contains))
                return route;
        }

        return null;
    }

    private static string Label(string route) => route switch
    {
        "/" => "Início",
        "/orcamento" => "Orçamento",
        "/raio-x" => "Raio-X",
        "/lancamentos" => "Lançamentos",
        "/categorias" => "Categorias",
        "/cartoes" => "Cartões",
        "/contas" => "Contas",
        _ => route,
    };

    private static Entry ReadEntry(string raw, string text, decimal? digits)
    {
        var type = text.Contains("aporte") || text.Contains("receita") || text.Contains("renda") || text.Contains("a receber")
            ? "Income"
            : text.Contains("despesa") || text.Contains("a pagar") || text.Contains("paguei") || text.Contains("paga")
                || text.Contains("conta de")
                ? "Expense"
                : "";
        var date = text.Contains("hoje") ? "hoje" : text.Contains("ontem") ? "ontem" : null;
        var description = ReadDescription(raw) ?? ReadChunk(raw, ReferenteRegex());
        var account = ReadChunk(raw, BankRegex()) ?? ReadAfter(text, "conta bancaria");
        var category = ReadAfter(text, "categoria");
        if (category is null && text.Contains("conta pessoal"))
            category = "Pessoal";
        return new Entry(type, digits ?? ReadWordAmount(text), description, category, account, date);
    }

    private static decimal? ReadWordAmount(string text)
    {
        var words = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["mil"] = 1000, ["novecentos"] = 900, ["oitocentos"] = 800, ["setecentos"] = 700,
            ["seiscentos"] = 600, ["quinhentos"] = 500, ["quatrocentos"] = 400, ["trezentos"] = 300,
            ["duzentos"] = 200, ["cento"] = 100, ["cem"] = 100, ["noventa"] = 90, ["oitenta"] = 80,
            ["setenta"] = 70, ["sessenta"] = 60, ["cinquenta"] = 50, ["quarenta"] = 40, ["trinta"] = 30,
            ["vinte"] = 20, ["dez"] = 10, ["nove"] = 9, ["oito"] = 8, ["sete"] = 7, ["seis"] = 6,
            ["cinco"] = 5, ["quatro"] = 4, ["tres"] = 3, ["dois"] = 2, ["um"] = 1,
        };
        decimal total = 0;
        var found = false;
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token is "e" or "reais" or "real")
                continue;
            if (words.TryGetValue(token, out var value))
            {
                found = true;
                total += value;
                continue;
            }
            if (found)
                break;
        }
        return found && total > 0 ? total : null;
    }

    private static string? ReadChunk(string raw, Regex pattern)
    {
        var match = pattern.Match(raw);
        if (!match.Success)
            return null;
        var value = match.Groups[1].Value.Trim().TrimEnd('.', ',');
        return value.Length == 0 ? null : value;
    }

    private static decimal? ReadAmount(string text)
    {
        var match = AmountRegex().Match(text);
        if (!match.Success)
            return null;
        var raw = match.Groups[1].Value.Replace(".", "").Replace(",", ".");
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value > 0
            ? decimal.Round(value, 2)
            : null;
    }

    private static string? ReadDescription(string text)
    {
        var match = DescriptionRegex().Match(text);
        if (!match.Success)
            return null;
        var description = match.Groups[1].Value.Trim();
        return description.Length == 0 ? null : description;
    }

    private static string? ReadAfter(string text, string label)
    {
        var index = text.IndexOf(label, StringComparison.Ordinal);
        if (index < 0)
            return null;
        var rest = text[(index + label.Length)..].Trim();
        if (rest.StartsWith("de ", StringComparison.Ordinal))
            rest = rest[3..];
        var stop = rest.IndexOf(" no ", StringComparison.Ordinal);
        if (stop > 0)
            rest = rest[..stop];
        rest = rest.Trim(' ', ',', '.');
        return rest.Length == 0 ? null : rest;
    }

    [GeneratedRegex(@"(\d{1,3}(?:\.\d{3})*(?:,\d{1,2})?|\d+(?:[.,]\d{1,2})?)\s*(?:reais|real)?", RegexOptions.CultureInvariant)]
    private static partial Regex AmountRegex();

    [GeneratedRegex(@"(?:conta de|lan[cç]\w+\s+(?:aqui\s+)?(?:uma|um)\s+)(.+?)\s+no valor", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DescriptionRegex();

    [GeneratedRegex(@"referente a\s+([^,\.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReferenteRegex();

    [GeneratedRegex(@"conta do\s+([^,\.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BankRegex();
}
