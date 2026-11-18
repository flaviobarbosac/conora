using Conora.Domain.Entities;
using Conora.Infrastructure.Persistence;
using Conora.Repository.Interface;
using Microsoft.EntityFrameworkCore;

namespace Conora.Repository;

public sealed class MonthShareRepository : IMonthShareRepository
{
    private readonly AppDbContext _context;

    public MonthShareRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<MonthShare?> FindAsync(
        Guid clientUsuarioId,
        Guid consultantUsuarioId,
        string competenceYm,
        CancellationToken ct = default)
        => _context.MonthShares.FirstOrDefaultAsync(
            s => s.DeletedAt == null
                 && s.ClientUsuarioId == clientUsuarioId
                 && s.ConsultantUsuarioId == consultantUsuarioId
                 && s.CompetenceYm == competenceYm,
            ct);

    public async Task<IReadOnlyList<SharedMonthRow>> ListForConsultantAsync(Guid consultantUsuarioId, CancellationToken ct = default)
    {
        return await (
            from share in _context.MonthShares.AsNoTracking()
            join user in _context.Users.IgnoreQueryFilters().AsNoTracking()
                on share.ClientUsuarioId equals user.Id
            where share.DeletedAt == null
                  && user.DeletedAt == null
                  && share.ConsultantUsuarioId == consultantUsuarioId
            orderby user.Name, share.CompetenceYm
            select new SharedMonthRow(user.Id, user.Name, user.Email, share.CompetenceYm)
        ).ToListAsync(ct);
    }

    public Task<List<MonthShare>> ListForClientAsync(Guid clientUsuarioId, CancellationToken ct = default)
        => _context.MonthShares
            .Where(share => share.DeletedAt == null && share.ClientUsuarioId == clientUsuarioId)
            .ToListAsync(ct);

    public void Add(MonthShare share) => _context.MonthShares.Add(share);
}
