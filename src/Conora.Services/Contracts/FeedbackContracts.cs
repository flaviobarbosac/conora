using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record CreateFeedbackRequest(string Modulo, string? Rota, string? Plataforma, int Nota, string Comentario);

public sealed record ReplyFeedbackRequest(string Resposta);

public sealed record FeedbackResponse(
    Guid Id,
    Guid UsuarioId,
    string UsuarioNome,
    string Modulo,
    string? Rota,
    string Plataforma,
    int Nota,
    string Comentario,
    DateTime CreatedAt,
    string? Resposta,
    DateTime? RespondidoEm,
    DateTime? RespostaLidaEm);

public sealed record ConsoleFeedbackResponse(
    Guid Id,
    Guid UsuarioId,
    string UsuarioNome,
    string Modulo,
    string? Rota,
    string Plataforma,
    int Nota,
    string Comentario,
    DateTime CreatedAt,
    FeedbackStatus Status,
    Guid? LidoPorUsuarioId,
    string? LidoPorUsuarioNome,
    DateTime? LidoEm,
    string? Resposta,
    Guid? RespondidoPorUsuarioId,
    string? RespondidoPorUsuarioNome,
    DateTime? RespondidoEm);

public sealed record ConsoleFeedbackUsuarioOption(Guid UsuarioId, string UsuarioNome);
