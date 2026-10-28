using Conora.Domain.Enums;

namespace Conora.Domain.Entities;

/// <summary>Product feedback from a signed-in user (stars + comment), with optional console reply.</summary>
public class UserFeedback : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string UsuarioNome { get; private set; } = default!;
    public string Modulo { get; private set; } = default!;
    public string? Rota { get; private set; }
    public int Nota { get; private set; }
    public string Comentario { get; private set; } = default!;
    public FeedbackStatus Status { get; private set; } = FeedbackStatus.Novo;

    public Guid? LidoPorUsuarioId { get; private set; }
    public string? LidoPorUsuarioNome { get; private set; }
    public DateTime? LidoEm { get; private set; }

    public string? Resposta { get; private set; }
    public Guid? RespondidoPorUsuarioId { get; private set; }
    public string? RespondidoPorUsuarioNome { get; private set; }
    public DateTime? RespondidoEm { get; private set; }
    public DateTime? RespostaLidaEm { get; private set; }

    private UserFeedback()
    {
    }

    public static UserFeedback Create(
        Guid usuarioId,
        string usuarioNome,
        string modulo,
        string? rota,
        int nota,
        string comentario) => new()
    {
        UsuarioId = usuarioId,
        UsuarioNome = usuarioNome.Trim(),
        Modulo = modulo.Trim(),
        Rota = string.IsNullOrWhiteSpace(rota) ? null : rota.Trim(),
        Nota = nota,
        Comentario = comentario.Trim(),
        Status = FeedbackStatus.Novo
    };

    public void MarkReadByAdmin(Guid adminId, string adminNome, DateTime when)
    {
        if (Status != FeedbackStatus.Novo)
            return;
        Status = FeedbackStatus.Lido;
        LidoPorUsuarioId = adminId;
        LidoPorUsuarioNome = adminNome;
        LidoEm = when;
        Version += 1;
    }

    public void Reply(Guid adminId, string adminNome, string resposta, DateTime when)
    {
        if (Status == FeedbackStatus.Novo)
        {
            LidoPorUsuarioId = adminId;
            LidoPorUsuarioNome = adminNome;
            LidoEm = when;
        }

        Resposta = resposta.Trim();
        RespondidoPorUsuarioId = adminId;
        RespondidoPorUsuarioNome = adminNome;
        RespondidoEm = when;
        Status = FeedbackStatus.Respondido;
        Version += 1;
    }

    public void MarkReplyRead(DateTime when)
    {
        if (Resposta is null || RespostaLidaEm is not null)
            return;
        RespostaLidaEm = when;
        Version += 1;
    }
}
