using Conora.Domain.Entities;

namespace Conora.Domain.Ports;

public interface IHotmartCourseStore
{
    Task<HotmartCoursePurchase?> FindByTransactionAsync(string transaction, CancellationToken ct = default);
    Task<bool> HasApprovedAsync(string email, CancellationToken ct = default);
    Task AddAsync(HotmartCoursePurchase purchase, CancellationToken ct = default);
}
