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
}
