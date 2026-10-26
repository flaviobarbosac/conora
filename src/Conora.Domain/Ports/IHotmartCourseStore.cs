using Conora.Domain.Entities;

namespace Conora.Domain.Ports;

public interface IHotmartCourseStore
{
    Task<HotmartCoursePurchase?> FindByTransactionAsync(string transaction, CancellationToken ct = default);
    /// <summary>True when this e-mail has an approved purchase of the given Hotmart product.</summary>
    Task<bool> HasApprovedAsync(string email, long productId, CancellationToken ct = default);
    Task AddAsync(HotmartCoursePurchase purchase, CancellationToken ct = default);
}
