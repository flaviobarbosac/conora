using Conora.Domain;
using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace Conora.Services;

public sealed class AuthService
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenant;
    private readonly ICorrelationContext _correlation;
    private readonly IPasswordHasher _passwords;
    private readonly ITokenService _tokens;
    private readonly IGoogleTokenValidator _google;
    private readonly IEmailSender _email;
    private readonly ChartAccountService _chartAccounts;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ITenantContext tenant,
        ICorrelationContext correlation,
        IPasswordHasher passwords,
        ITokenService tokens,
        IGoogleTokenValidator google,
        IEmailSender email,
        ChartAccountService chartAccounts,
        ILogger<AuthService> logger)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _audits = audits;
        _uow = uow;
        _tenant = tenant;
        _correlation = correlation;
        _passwords = passwords;
        _tokens = tokens;
        _google = google;
        _email = email;
        _chartAccounts = chartAccounts;
        _logger = logger;
    }

    public Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw new ValidationException("password", "Senha deve ter no mínimo 8 caracteres.");

        if (!BrazilianCpf.TryNormalize(request.Cpf, out var cpf))
            throw new ValidationException("cpf", "Informe um CPF válido.");

        var user = User.Create(request.Name, request.Email, cpf);
        user.SetPasswordHash(_passwords.Hash(request.Password));
        return IssueForNewUserAsync(user, "UserRegistered", ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var raw = (request.Usuario ?? request.Email ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            throw new ValidationException("usuario", "Informe CPF ou e-mail.");

        User? user;
        if (LoginIdentifier.IsEmail(raw))
        {
            user = await _users.FindByEmailAsync(raw.ToLowerInvariant(), ct);
        }
        else if (BrazilianCpf.TryNormalize(raw, out var cpf))
        {
            user = await _users.FindByCpfAsync(cpf, ct);
        }
        else
        {
            throw new ValidationException("usuario", "Informe um CPF ou e-mail válido.");
        }

        if (user is null || string.IsNullOrEmpty(user.PasswordHash) || !_passwords.Verify(request.Password, user.PasswordHash))
            throw new InvalidCredentialsException();

        return await IssueForExistingUserAsync(user, "UserLoggedIn", ct);
    }

    public async Task<AuthResponse> GoogleAsync(GoogleLoginRequest request, CancellationToken ct)
    {
        var identity = await _google.ValidateAsync(request.IdToken, ct);
        var user = await _users.FindByGoogleIdAsync(identity.GoogleId, ct)
                   ?? await _users.FindByEmailAsync(identity.Email, ct);

        if (user is null)
        {
            user = User.Create(identity.Name, identity.Email);
            user.SetGoogleId(identity.GoogleId);
            return await IssueForNewUserAsync(user, "UserGoogleRegistered", ct);
        }

        if (string.IsNullOrEmpty(user.GoogleId))
            user.SetGoogleId(identity.GoogleId);

        return await IssueForExistingUserAsync(user, "UserGoogleLoggedIn", ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var hash = _tokens.HashRefreshToken(request.RefreshToken);
        var stored = await _refreshTokens.FindActiveByHashAsync(hash, ct);
        if (stored is null)
            throw new InvalidTokenException();

        stored.Revoke();
        _tenant.Set(stored.UsuarioId);
        var user = await _users.GetByIdAsync(stored.UsuarioId, ct) ?? throw new InvalidTokenException();
        return await IssueForExistingUserAsync(user, "TokenRefreshed", ct);
    }

    public async Task LogoutAsync(CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            return;

        await _refreshTokens.RevokeAllForUserAsync(ct);
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<AuthResponse> IssueForNewUserAsync(User user, string action, CancellationToken ct)
    {
        _users.Add(user);
        _tenant.Set(user.Id);
        _chartAccounts.AddDefaults();
        return await PersistSessionAsync(user, action, notifyWelcome: true, ct);
    }

    private Task<AuthResponse> IssueForExistingUserAsync(User user, string action, CancellationToken ct)
    {
        _tenant.Set(user.Id);
        return PersistSessionAsync(user, action, notifyWelcome: false, ct);
    }

    private async Task<AuthResponse> PersistSessionAsync(User user, string action, bool notifyWelcome, CancellationToken ct)
    {
        var pair = _tokens.Issue(user.Id, user.Email);
        _refreshTokens.Add(RefreshToken.Create(_tokens.HashRefreshToken(pair.RefreshToken), DateTime.UtcNow.AddDays(14)));
        AuditRecorder.Record(_audits, _correlation, "User", user.Id, action, new { user.Email });

        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName.Contains("Email", StringComparison.OrdinalIgnoreCase))
        {
            throw new DuplicateEmailException(user.Email);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName.Contains("Cpf", StringComparison.OrdinalIgnoreCase))
        {
            throw new DuplicateCpfException();
        }

        if (notifyWelcome)
        {
            try
            {
                await _email.SendAsync(user.Email, "Bem-vindo ao Conora", "Sua conta no Conora foi criada.", ct);
            }
            catch (Exception ex)
            {
                // Registration must succeed even when SES/SMTP is unavailable (sandbox / DNS pending).
                _logger.LogWarning(ex, "Welcome email failed for {Email}; account created anyway.", user.Email);
            }
        }

        return new AuthResponse(user.Id, user.Email, pair.AccessToken, pair.RefreshToken, pair.AccessExpiresAtUtc);
    }
}
