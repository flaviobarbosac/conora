using System.Security.Cryptography;
using Conora.Domain;
using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;
using Microsoft.Extensions.Configuration;
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
    private readonly IAppleTokenValidator _apple;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly CategoryService _categories;
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
        IAppleTokenValidator apple,
        IEmailSender email,
        IConfiguration config,
        CategoryService categories,
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
        _apple = apple;
        _email = email;
        _config = config;
        _categories = categories;
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
        var user = await FindByLoginIdentifierAsync(request.Usuario ?? request.Email, ct);
        if (user is null)
            throw new InvalidCredentialsException();

        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            if (!string.IsNullOrEmpty(user.GoogleId) || !string.IsNullOrEmpty(user.AppleId))
                throw new ValidationException(
                    "usuario",
                    "Esta conta ainda não tem senha. Use Esqueci a senha para criar uma, ou entre com Google ou Apple.");
            throw new InvalidCredentialsException();
        }

        if (!_passwords.Verify(request.Password, user.PasswordHash))
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

    public async Task<AuthResponse> AppleAsync(AppleLoginRequest request, CancellationToken ct)
    {
        var identity = await _apple.ValidateAsync(request.IdentityToken, ct);
        var email = !string.IsNullOrWhiteSpace(identity.Email)
            ? identity.Email
            : request.Email?.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(identity.Email) && !identity.EmailVerified)
            throw new InvalidTokenException();

        var user = await _users.FindByAppleIdAsync(identity.AppleId, ct)
                   ?? (string.IsNullOrWhiteSpace(email) ? null : await _users.FindByEmailAsync(email, ct));

        if (user is null)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ValidationException("email", "Não foi possível obter o e-mail da Apple. Autorize o compartilhamento de e-mail.");

            var name = string.IsNullOrWhiteSpace(request.Name) ? email : request.Name.Trim();
            user = User.Create(name, email);
            user.SetAppleId(identity.AppleId);
            return await IssueForNewUserAsync(user, "UserAppleRegistered", ct);
        }

        if (string.IsNullOrEmpty(user.AppleId))
            user.SetAppleId(identity.AppleId);

        return await IssueForExistingUserAsync(user, "UserAppleLoggedIn", ct);
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

    public async Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        var user = await FindByLoginIdentifierAsync(request.Usuario, ct);
        if (user is null)
            return;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        user.IssuePasswordReset(_tokens.HashRefreshToken(token), DateTime.UtcNow.AddMinutes(30));
        _tenant.Set(user.Id);
        AuditRecorder.Record(_audits, _correlation, "User", user.Id, "PasswordResetRequested", new { user.Email });
        await _uow.SaveChangesAsync(ct);

        var link = $"{PublicWebBase()}/app/redefinir-senha?token={token}";
        try
        {
            await _email.SendAsync(user.Email, "Redefinir senha do Conora", new EmailContent(
                "Defina sua senha",
                [
                    "Recebemos um pedido para criar ou trocar a senha desta conta.",
                    "O link vale também para quem entrou com Google ou Apple. A senha nova passa a funcionar junto com o botão que você já usa."
                ])
            {
                Preheader = "Link para definir a senha da sua conta Conora. Vale por 30 minutos.",
                ActionLabel = "Definir senha",
                ActionUrl = link,
                Note = "O link expira em 30 minutos. Se você não pediu, ignore este e-mail."
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Password reset email failed for {Email}.", user.Email);
        }
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw new ValidationException("password", "Senha deve ter no mínimo 8 caracteres.");

        if (string.IsNullOrWhiteSpace(request.Token))
            throw new ValidationException("token", "Link inválido ou expirado. Peça outro em Esqueci a senha.");

        var tokenHash = _tokens.HashRefreshToken(request.Token.Trim());
        var user = await _users.FindByPasswordResetHashAsync(tokenHash, ct);
        if (user is null || !user.PasswordResetIsActive(tokenHash))
            throw new ValidationException("token", "Link inválido ou expirado. Peça outro em Esqueci a senha.");

        user.ApplyPassword(_passwords.Hash(request.Password));
        _tenant.Set(user.Id);
        await _refreshTokens.RevokeAllForUserAsync(ct);
        AuditRecorder.Record(_audits, _correlation, "User", user.Id, "PasswordResetCompleted", new { user.Email });
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<User?> FindByLoginIdentifierAsync(string? rawValue, CancellationToken ct)
    {
        var raw = (rawValue ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            throw new ValidationException("usuario", "Informe CPF ou e-mail.");

        if (LoginIdentifier.IsEmail(raw))
            return await _users.FindByEmailAsync(raw.ToLowerInvariant(), ct);

        if (BrazilianCpf.TryNormalize(raw, out var cpf))
            return await _users.FindByCpfAsync(cpf, ct);

        throw new ValidationException("usuario", "Informe um CPF ou e-mail válido.");
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
        _categories.AddDefaults();
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
                await _email.SendAsync(user.Email, "Bem-vindo ao Conora", new EmailContent(
                    $"Bem-vindo ao Conora, {user.Name}.",
                    ["Para aproveitar ao máximo, comece por estes três passos:"])
                {
                    Preheader = "Sua conta está pronta. Veja como dar os primeiros passos no Conora.",
                    Subtitle = "Sua vida financeira, com clareza. Sua conta já está pronta para uso.",
                    Steps =
                    [
                        new EmailStep("Cadastre suas contas", "Banco, cartão e carteira em um só painel."),
                        new EmailStep("Defina seu orçamento do mês", "Limites por categoria e alertas automáticos."),
                        new EmailStep("Convide sua família", "Acompanhem metas e despesas juntos.")
                    ],
                    ActionLabel = "Acessar minha conta",
                    ActionUrl = $"{PublicWebBase()}/app/"
                }, ct);
            }
            catch (Exception ex)
            {
                // Registration must succeed even when SES/SMTP is unavailable (sandbox / DNS pending).
                _logger.LogWarning(ex, "Welcome email failed for {Email}; account created anyway.", user.Email);
            }
        }

        return new AuthResponse(user.Id, user.Email, pair.AccessToken, pair.RefreshToken, pair.AccessExpiresAtUtc);
    }

    private string PublicWebBase()
    {
        var configured = _config["App:PublicWebUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');
        var cors = _config["Cors:Origins:0"];
        if (!string.IsNullOrWhiteSpace(cors))
            return cors.TrimEnd('/');
        return "https://conora.com.br";
    }
}
