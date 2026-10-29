using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Conora.Services;

public sealed class LgpdService
{
    public static readonly TimeSpan ExportRetention = TimeSpan.FromDays(30);

    private readonly IUserRepository _users;
    private readonly IAuditEventRepository _audits;
    private readonly ILgpdRequestRepository _requests;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenant;
    private readonly ICorrelationContext _correlation;
    private readonly IEmailSender _email;
    private readonly ILgpdExportStore _exports;
    private readonly ILgpdExportDataSource _exportData;
    private readonly FamilyGroupService _family;
    private readonly IAppleTokenRevoker _appleRevoker;
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LgpdService> _logger;

    public LgpdService(
        IUserRepository users,
        IAuditEventRepository audits,
        ILgpdRequestRepository requests,
        IRefreshTokenRepository refreshTokens,
        IUnitOfWork uow,
        ITenantContext tenant,
        ICorrelationContext correlation,
        IEmailSender email,
        ILgpdExportStore exports,
        ILgpdExportDataSource exportData,
        FamilyGroupService family,
        IAppleTokenRevoker appleRevoker,
        IServiceScopeFactory scopes,
        IConfiguration configuration,
        ILogger<LgpdService> logger)
    {
        _users = users;
        _audits = audits;
        _requests = requests;
        _refreshTokens = refreshTokens;
        _uow = uow;
        _tenant = tenant;
        _correlation = correlation;
        _email = email;
        _exports = exports;
        _exportData = exportData;
        _family = family;
        _appleRevoker = appleRevoker;
        _scopes = scopes;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Registers an export request only. File is built overnight (~03:00 BR).</summary>
    public async Task<LgpdExportRequestResponse> RequestExportAsync(CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        var pending = await _requests.FindPendingExportAsync(ct);
        if (pending is not null)
        {
            return new LgpdExportRequestResponse(
                pending.Id,
                pending.RequestedAtUtc,
                "A exportação será agendada e estará disponível pra você daqui a 24 horas.");
        }

        var request = LgpdRequest.Export();
        _requests.Add(request);
        AuditRecorder.Record(_audits, _correlation, "User", user.Id, "LgpdExportRequested", new { user.Email });
        await _uow.SaveChangesAsync(ct);

        return new LgpdExportRequestResponse(
            request.Id,
            request.RequestedAtUtc,
            "A exportação será agendada e estará disponível pra você daqui a 24 horas.");
    }

    public async Task<LgpdExportStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        await RequireUserAsync(ct);
        var pending = await _requests.FindPendingExportAsync(ct);
        var ready = await _requests.FindReadyExportAsync(ct);
        var canDelete = ready is not null && ready.IsExportReadyOnStorage;
        return new LgpdExportStatusResponse(
            ExportPending: pending is not null,
            ExportReady: canDelete,
            CanDeleteAccount: canDelete,
            ExpiresAtUtc: ready?.ExpiresAtUtc,
            Message: canDelete
                ? "Exportação disponível. Você já pode excluir a conta se quiser."
                : pending is not null
                    ? "Aguarde a exportação dos seus dados ficar disponível antes de excluir a conta."
                    : "Exporte seus dados e aguarde o arquivo ficar disponível antes de excluir a conta.");
    }

    public async Task<int> ProcessPendingExportsAsync(CancellationToken ct)
    {
        var pending = await _requests.ListPendingExportsAsync(100, ct);
        var done = 0;
        foreach (var item in pending)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                tenant.Set(item.UsuarioId);
                var service = scope.ServiceProvider.GetRequiredService<LgpdService>();
                await service.ProcessOneExportAsync(item.Id, ct);
                done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha ao processar exportação LGPD {RequestId} do usuário {UsuarioId}.",
                    item.Id, item.UsuarioId);
            }
        }

        return done;
    }

    private async Task ProcessOneExportAsync(Guid requestId, CancellationToken ct)
    {
        var request = await _requests.GetByIdAsync(requestId, ct);
        if (request is null || request.CompletedAtUtc is not null || request.Kind != "export")
            return;

        var user = await RequireUserAsync(ct);
        var sheets = await _exportData.LoadAsync(user.Id, user.Email, ct);
        var logo = LoadLogoBytes();
        var bytes = LgpdExportWorkbookBuilder.Build(sheets, logo);

        await using var stream = new MemoryStream(bytes);
        var objectKey = await _exports.SaveAsync(
            stream,
            $"conora-meus-dados-{user.Id:N}.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ct);

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var tokenHash = HashToken(rawToken);
        var expiresAt = DateTime.UtcNow.Add(ExportRetention);
        request.MarkExportReady(objectKey, expiresAt, tokenHash);

        var downloadUrl = BuildDownloadUrl(rawToken);
        var expiresLocal = FormatBrazilDate(expiresAt);

        await _email.SendAsync(
            user.Email,
            "Seus dados do Conora estão prontos",
            new EmailContent(
                "Exportação de dados",
                [
                    "Sua planilha com os dados do Conora foi gerada e está pronta para download.",
                    $"Seu arquivo estará disponível até {expiresLocal} e será excluído após essa data.",
                    "O link abaixo funciona uma única vez. Depois do primeiro download ele expira.",
                ])
            {
                Preheader = "Sua exportação LGPD está pronta para download.",
                ActionLabel = "Baixar meus dados",
                ActionUrl = downloadUrl,
                Note = $"Disponível até {expiresLocal}. Link de uso único."
            },
            ct);

        AuditRecorder.Record(_audits, _correlation, "User", user.Id, "LgpdExportCompleted",
            new { user.Email, request.Id, expiresAt });
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<(byte[] Content, string FileName)?> DownloadByTokenAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return null;

        var hash = HashToken(rawToken.Trim());
        var request = await _requests.FindByDownloadTokenHashAsync(hash, ct);
        if (request is null)
            return null;
        if (request.DownloadConsumedAtUtc is not null)
            return null;
        if (!request.IsExportReadyOnStorage || string.IsNullOrWhiteSpace(request.ExportObjectKey))
            return null;

        var bytes = await _exports.ReadAsync(request.ExportObjectKey, ct);
        if (bytes is null || bytes.Length == 0)
            return null;

        _tenant.Set(request.UsuarioId);
        request.ConsumeDownload();
        await _uow.SaveChangesAsync(ct);

        return (bytes, "conora-meus-dados.xlsx");
    }

    public async Task DeleteAsync(DeleteAccountRequest? body, CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        var ready = await _requests.FindReadyExportAsync(ct);
        if (ready is null || !ready.IsExportReadyOnStorage)
        {
            throw new ValidationException(
                "account",
                "Aguarde a exportação dos seus dados ficar disponível antes de excluir a conta.");
        }

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

    /// <summary>Removes S3 objects past the 30-day retention and clears storage metadata.</summary>
    public async Task<int> PurgeExpiredExportFilesAsync(CancellationToken ct)
    {
        var expired = await _requests.ListExpiredExportsAsync(100, ct);
        var done = 0;
        foreach (var item in expired)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                tenant.Set(item.UsuarioId);
                var requests = scope.ServiceProvider.GetRequiredService<ILgpdRequestRepository>();
                var store = scope.ServiceProvider.GetRequiredService<ILgpdExportStore>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var tracked = await requests.GetByIdAsync(item.Id, ct);
                if (tracked is null || string.IsNullOrWhiteSpace(tracked.ExportObjectKey))
                    continue;

                await store.DeleteAsync(tracked.ExportObjectKey, ct);
                tracked.ClearExportStorage();
                await uow.SaveChangesAsync(ct);
                done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha ao purgar exportação LGPD {RequestId}.", item.Id);
            }
        }

        return done;
    }

    private string BuildDownloadUrl(string rawToken)
    {
        var apiBase = _configuration["App:PublicApiUrl"]
            ?? _configuration["App:PublicWebUrl"]
            ?? "https://api.conora.com.br";
        return $"{apiBase.TrimEnd('/')}/lgpd/exports/download?t={Uri.EscapeDataString(rawToken)}";
    }

    private static string FormatBrazilDate(DateTime utc)
    {
        var tzId = OperatingSystem.IsWindows()
            ? "E. South America Standard Time"
            : "America/Sao_Paulo";
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
        return local.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
    }

    private static string HashToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static byte[]? LoadLogoBytes()
    {
        var asm = typeof(LgpdService).Assembly;
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("conora-logo.png", StringComparison.OrdinalIgnoreCase));
        if (name is null) return null;
        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private async Task<User> RequireUserAsync(CancellationToken ct)
    {
        if (!_tenant.UsuarioId.HasValue)
            throw new InvalidCredentialsException();

        return await _users.GetByIdAsync(_tenant.UsuarioId.Value, ct)
               ?? throw new UserNotFoundException(_tenant.UsuarioId.Value);
    }
}
