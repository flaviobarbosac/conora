using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class FeedbackService
{
    private readonly IUserFeedbackRepository _feedbacks;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenant;

    public FeedbackService(
        IUserFeedbackRepository feedbacks,
        IUserRepository users,
        IUnitOfWork uow,
        ITenantContext tenant)
    {
        _feedbacks = feedbacks;
        _users = users;
        _uow = uow;
        _tenant = tenant;
    }

    public async Task<FeedbackResponse> CreateAsync(CreateFeedbackRequest request, CancellationToken ct)
    {
        var usuarioId = RequireUserId();
        var modulo = (request.Modulo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(modulo) || modulo.Length > 80)
            throw new ValidationException("modulo", "Informe o módulo (até 80 caracteres).");

        var comentario = (request.Comentario ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(comentario))
            throw new ValidationException("comentario", "Comentário é obrigatório.");
        if (comentario.Length > 2000)
            throw new ValidationException("comentario", "Comentário deve ter no máximo 2000 caracteres.");

        if (request.Nota is < 1 or > 5)
            throw new ValidationException("nota", "Nota deve ser entre 1 e 5.");

        var user = await _users.GetByIdAsync(usuarioId, ct)
            ?? throw new UserNotFoundException(usuarioId);

        var rota = string.IsNullOrWhiteSpace(request.Rota) ? null : request.Rota.Trim();
        if (rota is { Length: > 200 })
            rota = rota[..200];

        var nome = string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name;
        var entity = UserFeedback.Create(usuarioId, nome, modulo, rota, request.Plataforma ?? "Web", request.Nota, comentario);
        await _feedbacks.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<IReadOnlyList<FeedbackResponse>> ListMineAsync(CancellationToken ct)
    {
        var usuarioId = RequireUserId();
        var items = await _feedbacks.ListMineAsync(usuarioId, 50, ct);
        return items.Select(ToResponse).ToList();
    }

    public async Task<FeedbackResponse?> GetMineAsync(Guid id, CancellationToken ct)
    {
        var usuarioId = RequireUserId();
        var entity = await _feedbacks.GetMineAsync(id, usuarioId, ct);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<FeedbackResponse?> MarkReplyReadAsync(Guid id, CancellationToken ct)
    {
        var usuarioId = RequireUserId();
        var entity = await _feedbacks.GetMineWithReplyAsync(id, usuarioId, ct);
        if (entity is null)
            return null;

        entity.MarkReplyRead(DateTime.UtcNow);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<IReadOnlyList<ConsoleFeedbackResponse>> ListAdminAsync(
        Guid? usuarioId,
        FeedbackStatus? status,
        DateTime? from,
        DateTime? to,
        string? modulo,
        int? nota,
        int? notaMin,
        int take,
        CancellationToken ct)
    {
        var items = await _feedbacks.ListAdminAsync(
            usuarioId, status, from, to, modulo, nota, notaMin, take, ct);
        return items.Select(ToConsoleResponse).ToList();
    }

    public async Task<IReadOnlyList<ConsoleFeedbackUsuarioOption>> ListAdminUsuariosAsync(CancellationToken ct)
    {
        var rows = await _feedbacks.ListAdminUsuariosAsync(ct);
        return rows.Select(r => new ConsoleFeedbackUsuarioOption(r.UsuarioId, r.UsuarioNome)).ToList();
    }

    public async Task<ConsoleFeedbackResponse?> GetAdminDetailAsync(
        Guid id,
        Guid adminId,
        string adminNome,
        CancellationToken ct)
    {
        var entity = await _feedbacks.GetAdminAsync(id, ct);
        if (entity is null)
            return null;

        // Cross-tenant write: ExecuteUpdate bypasses StampTenant (same pattern as family join).
        if (entity.Status == FeedbackStatus.Novo)
            entity = await _feedbacks.MarkReadAdminAsync(id, adminId, adminNome, DateTime.UtcNow, ct) ?? entity;

        return ToConsoleResponse(entity);
    }

    public async Task<ConsoleFeedbackResponse?> ReplyAdminAsync(
        Guid id,
        Guid adminId,
        string adminNome,
        ReplyFeedbackRequest request,
        CancellationToken ct)
    {
        var resposta = (request.Resposta ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(resposta))
            throw new ValidationException("resposta", "Resposta é obrigatória.");
        if (resposta.Length > 2000)
            throw new ValidationException("resposta", "Resposta deve ter no máximo 2000 caracteres.");

        var entity = await _feedbacks.ReplyAdminAsync(id, adminId, adminNome, resposta, DateTime.UtcNow, ct);
        return entity is null ? null : ToConsoleResponse(entity);
    }

    private Guid RequireUserId()
        => _tenant.UsuarioId ?? throw new UnauthorizedAccessException("Usuário não autenticado.");

    private static FeedbackResponse ToResponse(UserFeedback f) => new(
        f.Id,
        f.UsuarioId,
        f.UsuarioNome,
        f.Modulo,
        f.Rota,
        f.Plataforma,
        f.Nota,
        f.Comentario,
        f.CreatedAt,
        f.Resposta,
        f.RespondidoEm,
        f.RespostaLidaEm);

    private static ConsoleFeedbackResponse ToConsoleResponse(UserFeedback f) => new(
        f.Id,
        f.UsuarioId,
        f.UsuarioNome,
        f.Modulo,
        f.Rota,
        f.Plataforma,
        f.Nota,
        f.Comentario,
        f.CreatedAt,
        f.Status,
        f.LidoPorUsuarioId,
        f.LidoPorUsuarioNome,
        f.LidoEm,
        f.Resposta,
        f.RespondidoPorUsuarioId,
        f.RespondidoPorUsuarioNome,
        f.RespondidoEm);
}
