using System.Text.Json;
using System.Text.RegularExpressions;
using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

/// <summary>
/// Gemini assistant (server side only). Answers in pt-BR about the method and the app, never recommends buying or
/// selling assets, and degrades gracefully when no API key is configured.
/// </summary>
public sealed partial class GeminiService
{
    public const string UnavailableMessage =
        "O assistente de IA não está disponível no momento. O restante do Conora continua funcionando normalmente.";

    public const string TradeAdviceRefusal =
        "Não posso indicar compra ou venda de ativos nem recomendar investimentos. Posso ajudar a entender o método Raio X, seu orçamento e seus projetos de vida.";

    private const int MaxQuestionLength = 1000;
    private const int MaxQuestionsPerHour = 20;

    private const string SystemInstruction =
        "Você é o assistente do Conora, que aplica o método Raio X da Vida Financeira (diagnosticar, organizar, planejar, registrar, acompanhar, corrigir e construir). " +
        "Responda sempre em português do Brasil, de forma curta, clara e didática. " +
        "Ajude apenas com o método, o uso do aplicativo e a leitura dos números do usuário. " +
        "NUNCA recomende comprar ou vender ativos (ações, fundos, cripto, títulos) e nunca dê recomendação individual de investimento. " +
        "Responda estritamente em JSON no formato {\"answer\": \"texto\"}.";

    private readonly IGeminiClient _gemini;
    private readonly IFinanceRepository _repo;
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ICorrelationContext _correlation;
    private readonly DashboardService _dashboard;

    public GeminiService(
        IGeminiClient gemini,
        IFinanceRepository repo,
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        DashboardService dashboard)
    {
        _gemini = gemini;
        _repo = repo;
        _audits = audits;
        _uow = uow;
        _correlation = correlation;
        _dashboard = dashboard;
    }

    public async Task<AskAiResponse> AskAsync(AskAiRequest request, CancellationToken ct)
    {
        var question = request.Question?.Trim();
        if (string.IsNullOrWhiteSpace(question))
            throw new ValidationException("question", "Escreva sua pergunta.");
        if (question.Length > MaxQuestionLength)
            throw new ValidationException("question", $"A pergunta deve ter no máximo {MaxQuestionLength} caracteres.");

        if (!_gemini.IsConfigured)
            return new AskAiResponse(false, UnavailableMessage);

        if (LooksLikeTradeAdvice(question))
            return new AskAiResponse(true, TradeAdviceRefusal);

        var cutoff = DateTime.UtcNow.AddHours(-1);
        var used = await _repo.CountAsync<AuditEvent>(e => e.EntityName == "Gemini" && e.TimestampUtc > cutoff, ct);
        if (used >= MaxQuestionsPerHour)
            return new AskAiResponse(true, "Você atingiu o limite de perguntas por hora. Tente novamente mais tarde.");

        var prompt = await BuildPromptAsync(question, request.CompetenceYm, ct);
        var raw = await _gemini.GenerateJsonAsync(SystemInstruction, prompt, ct);

        // Usage is logged without the question/answer content (privacy).
        AuditRecorder.Record(_audits, _correlation, "Gemini", Guid.NewGuid(), "GeminiUsed", new { Length = question.Length, Ok = raw is not null });
        await _uow.SaveChangesAsync(ct);

        if (raw is null)
            return new AskAiResponse(false, UnavailableMessage);

        return new AskAiResponse(true, Sanitize(ExtractAnswer(raw)));
    }

    /// <summary>True when the text asks for, or gives, buy/sell guidance on financial assets.</summary>
    public static bool LooksLikeTradeAdvice(string text) => TradeAdviceRegex().IsMatch(text);

    /// <summary>Replaces any answer that carries asset trade advice by a safe refusal.</summary>
    public static string Sanitize(string answer)
        => string.IsNullOrWhiteSpace(answer) ? UnavailableMessage : LooksLikeTradeAdvice(answer) ? TradeAdviceRefusal : answer.Trim();

    private async Task<string> BuildPromptAsync(string question, string? competenceYm, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(competenceYm) || !Competence.IsValid(competenceYm))
            return question;

        var d = await _dashboard.GetAsync(competenceYm, ct);
        return $"Contexto do usuário em {d.CompetenceYm}: receita {d.IncomeTotal:0.00}, despesa {d.ExpenseTotal:0.00}, resultado {d.Result:0.00}.\nPergunta: {question}";
    }

    private static string ExtractAnswer(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("answer", out var answer)
                && answer.ValueKind == JsonValueKind.String)
                return answer.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            // Not JSON: fall back to the raw text below.
        }

        return raw;
    }

    [GeneratedRegex(
        @"\b(compr(e|ar|ando)|vend(a|e|er|endo)|invist(a|ir)\s+em)\b[^.!?\n]{0,80}\b(a[cç][aã]o|a[cç][oõ]es|fiis?|etfs?|bdrs?|cripto\w*|bitcoin|ethereum|tesouro|cdbs?|lcis?|lcas?|fundos\s+imobili\w+|ativos?|pap[eé]is)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TradeAdviceRegex();
}
