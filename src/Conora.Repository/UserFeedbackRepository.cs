using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Ports;
using Conora.Infrastructure.Persistence;
using Conora.Repository.Interface;
using Microsoft.EntityFrameworkCore;

namespace Conora.Repository;

public sealed class UserFeedbackRepository : BaseRepository<UserFeedback>, IUserFeedbackRepository
{
    public UserFeedbackRepository(AppDbContext context, ITenantContext tenant) : base(context, tenant)
    {
    }

    public async Task<IReadOnlyList<UserFeedback>> ListMineAsync(Guid usuarioId, int take, CancellationToken ct = default)
        => await Set.AsNoTracking()
            .Where(f => f.UsuarioId == usuarioId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public Task<UserFeedback?> GetMineAsync(Guid id, Guid usuarioId, CancellationToken ct = default)
        => Set.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id && f.UsuarioId == usuarioId, ct);

    public Task<UserFeedback?> GetMineWithReplyAsync(Guid id, Guid usuarioId, CancellationToken ct = default)
        => Set.FirstOrDefaultAsync(
            f => f.Id == id && f.UsuarioId == usuarioId && f.Resposta != null,
            ct);

    public async Task<IReadOnlyList<UserFeedback>> ListAdminAsync(
        Guid? usuarioId,
        FeedbackStatus? status,
        DateTime? from,
        DateTime? to,
        string? modulo,
        int? nota,
        int? notaMin,
        int take,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        var query = Set.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.DeletedAt == null);

        if (usuarioId.HasValue)
            query = query.Where(f => f.UsuarioId == usuarioId.Value);
        if (status.HasValue)
            query = query.Where(f => f.Status == status.Value);
        if (from.HasValue)
            query = query.Where(f => f.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(f => f.CreatedAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(modulo))
            query = query.Where(f => f.Modulo == modulo.Trim());
        if (nota is >= 1 and <= 5)
            query = query.Where(f => f.Nota == nota.Value);
        else if (notaMin is >= 1 and <= 5)
            query = query.Where(f => f.Nota >= notaMin.Value);

        return await query
            .OrderByDescending(f => f.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<(Guid UsuarioId, string UsuarioNome)>> ListAdminUsuariosAsync(
        CancellationToken ct = default)
    {
        var rows = await Set.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.DeletedAt == null)
            .GroupBy(f => new { f.UsuarioId, f.UsuarioNome })
            .Select(g => new { g.Key.UsuarioId, g.Key.UsuarioNome })
            .OrderBy(x => x.UsuarioNome)
            .ToListAsync(ct);
        return rows.Select(x => (x.UsuarioId, x.UsuarioNome)).ToList();
    }

    public Task<UserFeedback?> GetAdminAsync(Guid id, CancellationToken ct = default)
        => Set.IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == id && f.DeletedAt == null, ct);
}
