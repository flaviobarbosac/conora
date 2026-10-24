using Conora.Domain.Entities;
using Conora.Domain.Ports;
using Conora.Infrastructure.Billing;
using Conora.Repository.Interface;
using Microsoft.Extensions.Options;

namespace Conora.Services;

/// <summary>
/// Hotmart course sale grants the training-student trial (90 days via AccessPolicy), matched by buyer e-mail.
/// </summary>
public sealed class HotmartCourseService
{
    private readonly IHotmartCourseStore _purchases;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly HotmartOptions _options;

    public HotmartCourseService(
        IHotmartCourseStore purchases,
        IUserRepository users,
        IUnitOfWork uow,
        IOptions<HotmartOptions> options)
    {
        _purchases = purchases;
        _users = users;
        _uow = uow;
        _options = options.Value;
    }

    public async Task<bool> ReceiveAsync(
        string? eventName,
        string? email,
        string? transaction,
        long? productId,
        CancellationToken ct)
    {
        if (!HotmartCoursePurchase.GrantsStudentTrial(eventName))
            return false;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(transaction))
            return false;
        if (_options.ProductId > 0 && productId != _options.ProductId)
            return false;

        var normalized = email.Trim().ToLowerInvariant();
        var tx = transaction.Trim();
        var existing = await _purchases.FindByTransactionAsync(tx, ct);
        if (existing is null)
            await _purchases.AddAsync(HotmartCoursePurchase.CreateApproved(normalized, tx, productId), ct);
        else if (!existing.Approved)
            existing.Approved = true;

        var user = await _users.FindByEmailAsync(normalized, ct);
        if (user is not null)
            user.SetTrainingStudent(true);

        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
