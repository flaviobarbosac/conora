using Conora.Domain.Entities;
using Conora.Domain.Enums;

namespace Conora.Repository.Interface;

public interface IUserFeedbackRepository : IBaseRepository<UserFeedback>
{
    Task<IReadOnlyList<UserFeedback>> ListMineAsync(Guid usuarioId, int take, CancellationToken ct = default);
    Task<UserFeedback?> GetMineAsync(Guid id, Guid usuarioId, CancellationToken ct = default);
    Task<UserFeedback?> GetMineWithReplyAsync(Guid id, Guid usuarioId, CancellationToken ct = default);
    Task<IReadOnlyList<UserFeedback>> ListAdminAsync(
        Guid? usuarioId,
        FeedbackStatus? status,
        DateTime? from,
        DateTime? to,
        string? modulo,
        int? nota,
        int? notaMin,
        int take,
        CancellationToken ct = default);
    Task<IReadOnlyList<(Guid UsuarioId, string UsuarioNome)>> ListAdminUsuariosAsync(CancellationToken ct = default);
    Task<UserFeedback?> GetAdminAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Console mark-as-read across tenants. Uses ExecuteUpdate so StampTenant does not block
    /// the operator updating another user's feedback.
    /// </summary>
    Task<UserFeedback?> MarkReadAdminAsync(
        Guid id, Guid adminId, string adminNome, DateTime when, CancellationToken ct = default);

    /// <summary>
    /// Console reply across tenants. Uses ExecuteUpdate so StampTenant does not block
    /// the operator updating another user's feedback.
    /// </summary>
    Task<UserFeedback?> ReplyAdminAsync(
        Guid id, Guid adminId, string adminNome, string resposta, DateTime when, CancellationToken ct = default);
}
