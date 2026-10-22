using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class LgpdService
{
    private readonly IUserRepository _users;
    private readonly IAuditEventRepository _audits;
    private readonly ILgpdRequestRepository _requests;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenant;
    private readonly ICorrelationContext _correlation;
    private readonly IEmailSender _email;
    private readonly FamilyGroupService _family;
    private readonly IAppleTokenRevoker _appleRevoker;

    public LgpdService(
        IUserRepository users,
        IAuditEventRepository audits,
        ILgpdRequestRepository requests,
        IRefreshTokenRepository refreshTokens,
        IUnitOfWork uow,
        ITenantContext tenant,
        ICorrelationContext correlation,
        IEmailSender email,
        FamilyGroupService family,
        IAppleTokenRevoker appleRevoker)
    {
        _users = users;
        _audits = audits;
        _requests = requests;
        _refreshTokens = refreshTokens;
        _uow = uow;
        _tenant = tenant;
        _correlation = correlation;
        _email = email;
        _family = family;
        _appleRevoker = appleRevoker;
    }

    public async Task<LgpdExportResponse> ExportAsync(CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        var request = LgpdRequest.Export();
        _requests.Add(request);

        var (events, _) = await _audits.QueryAsync(null, null, 0, 500, ct);
        request.Complete();
        AuditRecorder.Record(_audits, _correlation, "User", user.Id, "LgpdExport", new { user.Email });
        await _uow.SaveChangesAsync(ct);

        return new LgpdExportResponse(user.Id, user.Email, user.Name, DateTime.UtcNow, new
        {
            user.Id,
            user.Name,
            user.Email,
            user.CreatedAt,
            Audit = events.Select(e => new { e.Action, e.TimestampUtc, e.DetailsJson })
        });
    }

    public async Task DeleteAsync(DeleteAccountRequest? body, CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        if (await _family.IsBudgetOwnerWithPeersAsync(ct))
            throw new ValidationException(
                "account",
                "Responsável pelo orçamento familiar precisa transferir o orçamento antes de excluir a conta.");

        var request = LgpdRequest.Deletion();
        _requests.Add(request);
        await _refreshTokens.RevokeAllForUserAsync(ct);
        user.RequestDeletion();
        request.Complete();
        AuditRecorder.Record(_audits, _correlation, "User", user.Id, "LgpdDeletion", new { user.Email });
        await _uow.SaveChangesAsync(ct);
        await RevokeAppleAsync(user, body, ct);
        await _email.SendAsync(
            user.Email,
            "Exclusão de dados",
            new EmailContent("Exclusão de dados", ["Seu pedido de exclusão foi registrado."])
            {
                Preheader = "Pedido de exclusão registrado no Conora."
            },
            ct);
    }

    // Apple requires the Sign in with Apple grant to be revoked when the account is deleted.
    private async Task RevokeAppleAsync(User user, DeleteAccountRequest? request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(user.AppleId) || string.IsNullOrWhiteSpace(request?.AppleAuthorizationCode))
            return;

        var client = string.Equals(request.AppleClient, "ios", StringComparison.OrdinalIgnoreCase)
            ? AppleClientKind.Ios
            : AppleClientKind.Web;
        var revoked = await _appleRevoker.RevokeAsync(request.AppleAuthorizationCode, client, request.AppleRedirectUri, ct);
        AuditRecorder.Record(_audits, _correlation, "User", user.Id, "AppleTokenRevoked", new { revoked, client = client.ToString() });
        await _uow.SaveChangesAsync(ct);
    }

    public async Task PurgeExpiredAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddYears(-5);
        var expired = await _users.ListExpiredDeletionsAsync(cutoff, ct);
        foreach (var user in expired)
            _users.Remove(user);

        if (expired.Count > 0)
            await _uow.SaveChangesAsync(ct);
    }

    private async Task<User> RequireUserAsync(CancellationToken ct)
    {
        if (!_tenant.UsuarioId.HasValue)
            throw new InvalidCredentialsException();

        return await _users.GetByIdAsync(_tenant.UsuarioId.Value, ct)
               ?? throw new UserNotFoundException(_tenant.UsuarioId.Value);
    }
}
