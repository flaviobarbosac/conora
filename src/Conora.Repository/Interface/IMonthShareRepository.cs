using Conora.Domain.Entities;

namespace Conora.Repository.Interface;

public sealed record SharedMonthRow(Guid ClientUsuarioId, string Name, string Email, string CompetenceYm);

public interface IMonthShareRepository
{
    Task<MonthShare?> FindAsync(Guid clientUsuarioId, Guid consultantUsuarioId, string competenceYm, CancellationToken ct = default);
    Task<IReadOnlyList<SharedMonthRow>> ListForConsultantAsync(Guid consultantUsuarioId, CancellationToken ct = default);
    Task<List<MonthShare>> ListForClientAsync(Guid clientUsuarioId, CancellationToken ct = default);
    void Add(MonthShare share);
}
