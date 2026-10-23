using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Ports;
using Conora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Conora.Infrastructure.Billing;

/// <summary>
/// Local stub: marks Individual workspace_subscriptions as ReadOnly.
/// TODO: call Google Play Developer API / App Store Server API to cancel the store SKU,
/// then confirm via RTDN / ASN webhook before mutating state.
/// </summary>
public sealed class LocalStoreBillingGateway : IStoreBillingGateway
{
    private readonly AppDbContext _db;
    private readonly ILogger<LocalStoreBillingGateway> _logger;

    public LocalStoreBillingGateway(AppDbContext db, ILogger<LocalStoreBillingGateway> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task CancelMemberIndividualAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var sub = await _db.WorkspaceSubscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.UsuarioId == usuarioId && s.DeletedAt == null, ct);
        if (sub is null)
            return;
        if (sub.Tier != PlanTier.Individual)
            return;

        // TODO: POST store cancel for the purchase token / originalTransactionId, then await webhook.
        sub.MarkReadOnly();
        _logger.LogInformation(
            "Cancelled local Individual subscription {SubId} for user {UserId} on family invite accept (store API stub).",
            sub.Id, usuarioId);
        await _db.SaveChangesAsync(ct);
    }

    public async Task CancelOwnerSubscriptionAsync(Guid ownerUsuarioId, CancellationToken ct = default)
    {
        var sub = await _db.WorkspaceSubscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.UsuarioId == ownerUsuarioId && s.DeletedAt == null, ct);
        if (sub is null)
            return;

        // TODO: cancel Duo/Family SKU on Play / App Store, then confirm via webhook.
        sub.MarkReadOnly();
        _logger.LogInformation(
            "Cancelled local owner subscription {SubId} (tier {Tier}) for user {UserId} on group leave (store API stub).",
            sub.Id, sub.Tier, ownerUsuarioId);
        await _db.SaveChangesAsync(ct);
    }
}
