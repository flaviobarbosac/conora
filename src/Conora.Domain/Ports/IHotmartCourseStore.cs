using Conora.Domain.Entities;

namespace Conora.Domain.Ports;

public interface IHotmartCourseStore
{
    Task<HotmartCoursePurchase?> FindByTransactionAsync(string transaction, CancellationToken ct = default);
    /// <summary>True when this e-mail already has the 8th-day release for the product.</summary>
    Task<bool> HasApprovedAsync(string email, long productId, CancellationToken ct = default);
    Task<IReadOnlyList<HotmartCoursePurchase>> ListByEmailAsync(string email, long productId, CancellationToken ct = default);
    /// <summary>Purchases whose 8th day has arrived and still need the grant or the release e-mail.</summary>
    Task<IReadOnlyList<HotmartCoursePurchase>> ListAwaitingReleaseAsync(DateTime utcNow, CancellationToken ct = default);
    Task AddAsync(HotmartCoursePurchase purchase, CancellationToken ct = default);

    Task<HotmartStudentPage> ListStudentsAsync(HotmartStudentQuery query, CancellationToken ct = default);
}

public sealed record HotmartStudentQuery(
    long ProductId,
    string? Email,
    string? Name,
    string? Status,
    DateTime? AcquiredFrom,
    DateTime? AcquiredTo,
    DateTime? ReleasedFrom,
    DateTime? ReleasedTo,
    int Page,
    int PageSize);

public sealed record HotmartStudentPage(
    IReadOnlyList<HotmartStudentRow> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record HotmartStudentRow(
    string Email,
    string? Name,
    bool EmailMatches,
    string Status,
    DateTime AcquiredAt,
    DateTime? ReleasedAt,
    DateTime? AccessEndsAt);
