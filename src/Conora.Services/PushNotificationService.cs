using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class PushNotificationService
{
    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenant;
    private readonly ICorrelationContext _correlation;
    private readonly IAuditEventRepository _audits;
    private readonly IPushNotificationSender _sender;

    public PushNotificationService(
        IFinanceRepository repo,
        IUnitOfWork uow,
        ITenantContext tenant,
        ICorrelationContext correlation,
        IAuditEventRepository audits,
        IPushNotificationSender sender)
    {
        _repo = repo;
        _uow = uow;
        _tenant = tenant;
        _correlation = correlation;
        _audits = audits;
        _sender = sender;
    }

    public bool IsConfigured => _sender.IsConfigured;

    public async Task<IReadOnlyList<PushTokenResponse>> ListMineAsync(CancellationToken ct)
    {
        var rows = await _repo.ListAsync<DevicePushToken>(null, ct, track: false);
        return rows
            .OrderByDescending(r => r.LastSeenAt)
            .Select(r => new PushTokenResponse(r.Id, r.Token, r.Platform, r.LastSeenAt))
            .ToList();
    }

    public async Task<PushTokenResponse> RegisterAsync(RegisterPushTokenRequest request, CancellationToken ct)
    {
        var token = DevicePushToken.NormalizeToken(request.Token);
        var platform = DevicePushToken.NormalizePlatform(request.Platform);
        var userId = _tenant.UsuarioId ?? throw new UnauthorizedAccessException("Tenant não autenticado.");

        // Token is globally unique: if another account had it, move it to the current user.
        var existing = await _repo.FirstOrDefaultGlobalByKeyAsync<DevicePushToken>(
            t => t.Token == token,
            ct);

        if (existing is not null)
        {
            if (existing.UsuarioId != userId)
            {
                _repo.SoftDelete(existing);
                var moved = DevicePushToken.Register(token, platform);
                _repo.Add(moved);
                AuditRecorder.Record(_audits, _correlation, "DevicePushToken", moved.Id, "PushTokenRegistered",
                    new { platform, Reassigned = true });
                await _uow.SaveChangesAsync(ct);
                return new PushTokenResponse(moved.Id, moved.Token, moved.Platform, moved.LastSeenAt);
            }

            existing.Touch(platform);
            await _uow.SaveChangesAsync(ct);
            return new PushTokenResponse(existing.Id, existing.Token, existing.Platform, existing.LastSeenAt);
        }

        var created = DevicePushToken.Register(token, platform);
        _repo.Add(created);
        AuditRecorder.Record(_audits, _correlation, "DevicePushToken", created.Id, "PushTokenRegistered",
            new { platform });
        await _uow.SaveChangesAsync(ct);
        return new PushTokenResponse(created.Id, created.Token, created.Platform, created.LastSeenAt);
    }

    public async Task UnregisterAsync(UnregisterPushTokenRequest request, CancellationToken ct)
    {
        var token = DevicePushToken.NormalizeToken(request.Token);
        var row = await _repo.FirstOrDefaultAsync<DevicePushToken>(t => t.Token == token, ct, track: true);
        if (row is null)
            return;

        _repo.SoftDelete(row);
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>Sends a push to the current user's registered devices (smoke test).</summary>
    public async Task<SendPushResponse> SendTestAsync(SendPushRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Body))
            throw new ValidationException("title", "Informe título e corpo da notificação.");

        var mine = await _repo.ListAsync<DevicePushToken>(null, ct, track: true);
        return await DeliverAsync(mine, request.Title.Trim(), request.Body.Trim(), request.Data, ct);
    }

    /// <summary>
    /// Sends to devices of the given users. When <paramref name="request.UserIds"/> is null/empty,
    /// sends to the current tenant only.
    /// </summary>
    public async Task<SendPushResponse> SendAsync(SendPushRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Body))
            throw new ValidationException("title", "Informe título e corpo da notificação.");

        IReadOnlyList<DevicePushToken> rows;
        if (request.UserIds is { Count: > 0 } userIds)
        {
            var unique = userIds.Distinct().ToList();
            rows = await _repo.ListForUsuariosAsync<DevicePushToken>(
                unique,
                _ => true,
                ct,
                track: true);
        }
        else
        {
            rows = await _repo.ListAsync<DevicePushToken>(null, ct, track: true);
        }

        return await DeliverAsync(rows, request.Title.Trim(), request.Body.Trim(), request.Data, ct);
    }

    private async Task<SendPushResponse> DeliverAsync(
        IReadOnlyList<DevicePushToken> rows,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data,
        CancellationToken ct)
    {
        if (!_sender.IsConfigured)
        {
            return new SendPushResponse(
                Configured: false,
                Attempted: rows.Count,
                Succeeded: 0,
                Failed: rows.Count,
                TokensRemoved: 0,
                Detail: "FCM não configurado. Defina Fcm:ProjectId e Fcm:CredentialsJson (ou CredentialsPath).");
        }

        if (rows.Count == 0)
        {
            return new SendPushResponse(
                Configured: true,
                Attempted: 0,
                Succeeded: 0,
                Failed: 0,
                TokensRemoved: 0,
                Detail: "Nenhum token de dispositivo registrado para o destinatário.");
        }

        var result = await _sender.SendAsync(
            rows.Select(r => r.Token).ToList(),
            new PushMessage(title, body, data),
            ct);

        var removed = 0;
        if (result.InvalidTokens.Count > 0)
        {
            var invalid = result.InvalidTokens.ToHashSet(StringComparer.Ordinal);
            foreach (var row in rows.Where(r => invalid.Contains(r.Token)))
            {
                _repo.SoftDelete(row);
                removed++;
            }

            if (removed > 0)
                await _uow.SaveChangesAsync(ct);
        }

        AuditRecorder.Record(_audits, _correlation, "DevicePushToken", Guid.Empty, "PushSent",
            new { result.Attempted, result.Succeeded, result.Failed, removed });

        return new SendPushResponse(
            Configured: true,
            Attempted: result.Attempted,
            Succeeded: result.Succeeded,
            Failed: result.Failed,
            TokensRemoved: removed);
    }
}
