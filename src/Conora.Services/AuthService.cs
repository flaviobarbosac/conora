using System.Security.Cryptography;
using Conora.Domain;
using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Infrastructure.Billing;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conora.Services;

public sealed class AuthService
{
    private static readonly TimeSpan PreRegistrationTtl = TimeSpan.FromMinutes(10);
    /** After the first successful confirmation, the same link can open the session again for this long (capped by the 10-minute TTL). */
    private static readonly TimeSpan PreRegistrationReuseWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PasswordResetTokenTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PasswordResetCoolDown = TimeSpan.FromMinutes(15);

    private readonly IUserRepository _users;
    private readonly IPreRegistrationRepository _preRegistrations;
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
    private readonly IHostEnvironment _env;
    private readonly IHotmartCourseStore _hotmart;
    private readonly HotmartOptions _hotmartOptions;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository users,
        IPreRegistrationRepository preRegistrations,
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
        IHostEnvironment env,
        IHotmartCourseStore hotmart,
        IOptions<HotmartOptions> hotmartOptions,
        ILogger<AuthService> logger)
    {
        _users = users;
        _preRegistrations = preRegistrations;
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
        _env = env;
        _hotmart = hotmart;
        _hotmartOptions = hotmartOptions.Value;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw new ValidationException("password", "Senha deve ter no mínimo 8 caracteres.");

        if (!BrazilianCpf.TryNormalize(request.Cpf, out var cpf))
            throw new ValidationException("cpf", "Informe um CPF válido.");

        var email = request.Email.Trim().ToLowerInvariant();
        if (await _users.FindByEmailAsync(email, ct) is not null)
            throw new DuplicateEmailException(email);
        if (await _users.FindByCpfAsync(cpf, ct) is not null)
            throw new DuplicateCpfException();

        if (_env.IsEnvironment("Testing"))
        {
            var user = User.Create(request.Name, email, cpf);
            user.SetPasswordHash(_passwords.Hash(request.Password));
            await ApplyTrainingFlagAsync(user, ct);
            return await CreateVerifiedUserAsync(user, "UserRegistered", notifyWelcome: false, ct);
        }

        await EnsurePreRegistrationSlotAsync(email, googleId: null, appleId: null, ct);

        var token = NewRawToken();
        var pending = PreRegistration.CreatePassword(
            request.Name,
            email,
            cpf,
            _passwords.Hash(request.Password),
            _tokens.HashRefreshToken(token),
            DateTime.UtcNow.Add(PreRegistrationTtl),
            await IsTrainingStudentAsync(email, ct));
        if (_env.IsDevelopment())
            return await ConsumeLocalConfirmationAsync(pending, token, ct);

        _preRegistrations.Add(pending);
        await _uow.SaveChangesAsync(ct);
        await SendPreRegistrationEmailAsync(email, token, ct);
        throw new ValidationException("email", PreRegistrationEmailSentMessage(email));
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await FindByLoginIdentifierAsync(request.Usuario ?? request.Email, ct);
        if (user is null)
            throw new AccountNotFoundException();

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
        var user = await _users.FindByGoogleIdAsync(identity.GoogleId, ct);
        var register = IsRegisterIntent(request.Intent);

        if (register)
        {
            if (user is not null)
                throw new SocialAccountExistsException("Google");

            var email = identity.Email.Trim().ToLowerInvariant();
            if (await _users.FindByEmailAsync(email, ct) is not null)
                throw new DuplicateEmailException(email);

            if (_env.IsEnvironment("Testing"))
            {
                var created = User.Create(identity.Name, email);
                created.SetGoogleId(identity.GoogleId);
                await ApplyTrainingFlagAsync(created, ct);
                return await CreateVerifiedUserAsync(created, "UserGoogleRegistered", notifyWelcome: false, ct);
            }

            await EnsurePreRegistrationSlotAsync(email, identity.GoogleId, appleId: null, ct);

            var token = NewRawToken();
            var pending = PreRegistration.CreateGoogle(
                identity.Name,
                email,
                identity.GoogleId,
                _tokens.HashRefreshToken(token),
                DateTime.UtcNow.Add(PreRegistrationTtl),
                await IsTrainingStudentAsync(email, ct));
            if (_env.IsDevelopment())
                return await ConsumeLocalConfirmationAsync(pending, token, ct);

            _preRegistrations.Add(pending);
            await _uow.SaveChangesAsync(ct);
            await SendPreRegistrationEmailAsync(email, token, ct);
            throw new ValidationException("email", PreRegistrationEmailSentMessage(email));
        }

        if (user is null)
            throw new AccountNotFoundException();

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

        var user = await _users.FindByAppleIdAsync(identity.AppleId, ct);
        var register = IsRegisterIntent(request.Intent);

        if (register)
        {
            if (user is not null)
                throw new SocialAccountExistsException("Apple");

            if (string.IsNullOrWhiteSpace(email))
                throw new ValidationException("email", "Não foi possível obter o e-mail da Apple. Autorize o compartilhamento de e-mail.");

            email = email.Trim().ToLowerInvariant();
            if (await _users.FindByEmailAsync(email, ct) is not null)
                throw new DuplicateEmailException(email);

            var name = string.IsNullOrWhiteSpace(request.Name) ? email : request.Name.Trim();

            if (_env.IsEnvironment("Testing"))
            {
                var created = User.Create(name, email);
                created.SetAppleId(identity.AppleId);
                await ApplyTrainingFlagAsync(created, ct);
                return await CreateVerifiedUserAsync(created, "UserAppleRegistered", notifyWelcome: false, ct);
            }

            await EnsurePreRegistrationSlotAsync(email, googleId: null, identity.AppleId, ct);

            var token = NewRawToken();
            var pending = PreRegistration.CreateApple(
                name,
                email,
                identity.AppleId,
                _tokens.HashRefreshToken(token),
                DateTime.UtcNow.Add(PreRegistrationTtl),
                await IsTrainingStudentAsync(email, ct));
            if (_env.IsDevelopment())
                return await ConsumeLocalConfirmationAsync(pending, token, ct);

            _preRegistrations.Add(pending);
            await _uow.SaveChangesAsync(ct);
            await SendPreRegistrationEmailAsync(email, token, ct);
            throw new ValidationException("email", PreRegistrationEmailSentMessage(email));
        }

        if (user is null)
            throw new AccountNotFoundException();

        return await IssueForExistingUserAsync(user, "UserAppleLoggedIn", ct);
    }

    /// <summary>
    /// Development only. Saves the confirmation link and consumes it at once, so localhost
    /// creates the account without an inbox. Production still sends the e-mail.
    /// </summary>
    private async Task<AuthResponse> ConsumeLocalConfirmationAsync(
        PreRegistration pending,
        string rawToken,
        CancellationToken ct)
    {
        _preRegistrations.Add(pending);
        await _uow.SaveChangesAsync(ct);
        return await VerifyEmailAsync(new VerifyEmailRequest(rawToken), ct);
    }

    private static string PreRegistrationEmailSentMessage(string email)
        => $"Enviamos um e-mail de validação para {email}. Abra a mensagem e clique no link em até 10 minutos para criar sua conta.";

    private static bool IsRegisterIntent(string? intent)
        => string.Equals(intent?.Trim(), "register", StringComparison.OrdinalIgnoreCase);

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var hash = _tokens.HashRefreshToken(request.RefreshToken);
        var stored = await _refreshTokens.FindActiveByHashAsync(hash, ct);
        if (stored is null)
            throw new InvalidTokenException();

        stored.Revoke();
        _tenant.Set(stored.UsuarioId);
        var user = await _users.GetByIdAsync(stored.UsuarioId, ct) ?? throw new InvalidTokenException();
        if (!user.EmailVerified)
            throw new ValidationException(
                "email",
                "Confirme seu e-mail pelo link que enviamos (válido por 10 minutos) para entrar.");
        return await PersistSessionAsync(user, "TokenRefreshed", notifyWelcome: false, ct);
    }

    public async Task<AuthResponse> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            throw new ValidationException("token", "Link inválido ou expirado. Cadastre-se de novo para receber outro.");

        var tokenHash = _tokens.HashRefreshToken(request.Token.Trim());
        var now = DateTime.UtcNow;

        var pending = await _preRegistrations.FindByTokenHashAsync(tokenHash, ct);
        if (pending is not null)
        {
            // Already promoted: reuse window (1 minute, still inside the 10-minute TTL).
            if (pending.PromotedUserId is Guid promotedId)
            {
                if (pending.CanReuse(tokenHash, now, PreRegistrationReuseWindow))
                {
                    var promoted = await _users.GetByIdAsync(promotedId, ct);
                    if (promoted is not null)
                        return await PersistSessionAsync(promoted, "EmailVerifiedReuse", notifyWelcome: false, ct);
                }

                _preRegistrations.Remove(pending);
                await _uow.SaveChangesAsync(ct);
                // Account already exists — send the person to login instead of re-register.
                throw new ValidationException(
                    "token",
                    "Este link já foi usado. Entre com seu e-mail e senha.");
            }

            if (!pending.IsActive(tokenHash))
            {
                _preRegistrations.Remove(pending);
                await _uow.SaveChangesAsync(ct);
                throw new ValidationException("token", "Link expirado. Cadastre-se de novo — o link vale por 10 minutos.");
            }

            return await PromotePreRegistrationAsync(pending, ct);
        }

        // Legacy path: user created before pre-registration (e-mail hash on users).
        var user = await _users.FindByEmailVerificationHashAsync(tokenHash, ct);
        if (user is null || !user.EmailVerificationIsActive(tokenHash))
            throw new ValidationException("token", "Link inválido ou expirado. Cadastre-se de novo para receber outro.");

        _tenant.Set(user.Id);
        if (!user.EmailVerified)
        {
            user.MarkEmailVerified();
            AuditRecorder.Record(_audits, _correlation, "User", user.Id, "EmailVerified", new { user.Email });
        }

        return await PersistSessionAsync(user, "EmailVerifiedLogin", notifyWelcome: true, ct);
    }

    public async Task<int> PurgeExpiredPreRegistrationsAsync(CancellationToken ct)
        => await _preRegistrations.DeleteExpiredAsync(DateTime.UtcNow, ct);

    public async Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        var user = await FindByLoginIdentifierAsync(request.Usuario, ct);
        if (user is null)
            return;

        if (user.PasswordResetExpiresAtUtc is DateTime expiresAt)
        {
            var issuedAt = expiresAt - PasswordResetTokenTtl;
            if (DateTime.UtcNow - issuedAt < PasswordResetCoolDown)
            {
                AuditRecorder.Record(
                    _audits,
                    _correlation,
                    "User",
                    user.Id,
                    "PasswordResetThrottled",
                    new { user.Email, ip = _correlation.ClientIp });
                await _uow.SaveChangesAsync(ct);
                throw new ValidationException(
                    "usuario",
                    "Já enviamos um link há pouco. Aguarde alguns minutos antes de pedir outro.");
            }
        }

        var token = NewRawToken();
        user.IssuePasswordReset(_tokens.HashRefreshToken(token), DateTime.UtcNow.Add(PasswordResetTokenTtl));
        _tenant.Set(user.Id);
        AuditRecorder.Record(
            _audits,
            _correlation,
            "User",
            user.Id,
            "PasswordResetRequested",
            new { user.Email, ip = _correlation.ClientIp });
        await _uow.SaveChangesAsync(ct);

        var link = AuthLink("redefinir-senha", token);
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
            throw new ValidationException("usuario", "Não foi possível enviar o e-mail. Tente de novo.");
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

    private async Task EnsurePreRegistrationSlotAsync(
        string email,
        string? googleId,
        string? appleId,
        CancellationToken ct)
    {
        var removed = new HashSet<Guid>();
        void Drop(PreRegistration? row)
        {
            if (row is null || !removed.Add(row.Id))
                return;
            _preRegistrations.Remove(row);
        }

        Drop(await _preRegistrations.FindByEmailAsync(email, ct));
        if (!string.IsNullOrEmpty(googleId))
            Drop(await _preRegistrations.FindByGoogleIdAsync(googleId, ct));
        if (!string.IsNullOrEmpty(appleId))
            Drop(await _preRegistrations.FindByAppleIdAsync(appleId, ct));

        if (removed.Count > 0)
            await _uow.SaveChangesAsync(ct);
    }

    private async Task<AuthResponse> PromotePreRegistrationAsync(PreRegistration pending, CancellationToken ct)
    {
        // Race: another device already promoted this e-mail — log in if still inside the reuse window.
        var existing = await _users.FindByEmailAsync(pending.Email, ct);
        if (existing is not null)
        {
            pending.MarkConsumed(existing.Id, DateTime.UtcNow);
            await _uow.SaveChangesAsync(ct);
            if (pending.CanReuse(pending.TokenHash, DateTime.UtcNow, PreRegistrationReuseWindow))
                return await PersistSessionAsync(existing, "EmailVerifiedReuse", notifyWelcome: false, ct);
            _preRegistrations.Remove(pending);
            await _uow.SaveChangesAsync(ct);
            throw new ValidationException("token", "Este link já foi usado. Entre com seu e-mail e senha.");
        }

        if (!string.IsNullOrEmpty(pending.GoogleId)
            && await _users.FindByGoogleIdAsync(pending.GoogleId, ct) is not null)
        {
            _preRegistrations.Remove(pending);
            await _uow.SaveChangesAsync(ct);
            throw new SocialAccountExistsException("Google");
        }

        if (!string.IsNullOrEmpty(pending.AppleId)
            && await _users.FindByAppleIdAsync(pending.AppleId, ct) is not null)
        {
            _preRegistrations.Remove(pending);
            await _uow.SaveChangesAsync(ct);
            throw new SocialAccountExistsException("Apple");
        }

        if (!string.IsNullOrEmpty(pending.Cpf)
            && await _users.FindByCpfAsync(pending.Cpf, ct) is not null)
        {
            _preRegistrations.Remove(pending);
            await _uow.SaveChangesAsync(ct);
            throw new DuplicateCpfException();
        }

        var user = User.Create(pending.Name, pending.Email, pending.Cpf);
        if (!string.IsNullOrEmpty(pending.PasswordHash))
            user.SetPasswordHash(pending.PasswordHash);
        if (!string.IsNullOrEmpty(pending.GoogleId))
            user.SetGoogleId(pending.GoogleId);
        if (!string.IsNullOrEmpty(pending.AppleId))
            user.SetAppleId(pending.AppleId);
        if (pending.IsTrainingStudent)
            user.SetTrainingStudent(true);
        user.MarkEmailVerified();

        var action = !string.IsNullOrEmpty(pending.GoogleId) ? "UserGoogleRegistered"
            : !string.IsNullOrEmpty(pending.AppleId) ? "UserAppleRegistered"
            : "UserRegistered";

        // Keep the pre-registration row for the 1-minute reuse window (browser + app).
        var auth = await CreateVerifiedUserAsync(user, action, notifyWelcome: true, ct);
        pending.MarkConsumed(user.Id, DateTime.UtcNow);
        await _uow.SaveChangesAsync(ct);
        return auth;
    }

    private async Task<AuthResponse> CreateVerifiedUserAsync(
        User user,
        string action,
        bool notifyWelcome,
        CancellationToken ct)
    {
        user.MarkEmailVerified();
        _users.Add(user);
        _tenant.Set(user.Id);
        AuditRecorder.Record(_audits, _correlation, "User", user.Id, action, new { user.Email, ip = _correlation.ClientIp });
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
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName.Contains("GoogleId", StringComparison.OrdinalIgnoreCase))
        {
            throw new SocialAccountExistsException("Google");
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName.Contains("AppleId", StringComparison.OrdinalIgnoreCase))
        {
            throw new SocialAccountExistsException("Apple");
        }

        return await PersistSessionAsync(user, "EmailVerifiedLogin", notifyWelcome, ct);
    }

    private Task<AuthResponse> IssueForExistingUserAsync(User user, string action, CancellationToken ct)
    {
        _tenant.Set(user.Id);
        if (!user.EmailVerified)
        {
            // Legacy unverified user rows: ask them to register again under the new flow.
            throw new ValidationException(
                "email",
                "Esta conta ainda não confirmou o e-mail. Cadastre-se de novo para receber um link válido por 10 minutos.");
        }

        return PersistSessionAsync(user, action, notifyWelcome: false, ct);
    }

    private async Task SendPreRegistrationEmailAsync(string email, string rawToken, CancellationToken ct)
    {
        var link = AuthLink("validar-email", rawToken);
        try
        {
            await _email.SendAsync(email, "Valide seu e-mail no Conora", new EmailContent(
                "Confirme seu e-mail",
                [
                    "Para criar sua conta no Conora, confirme este endereço clicando no botão abaixo.",
                    "Sem essa validação a conta não é criada."
                ])
            {
                Preheader = "Link válido por 10 minutos para criar sua conta Conora.",
                ActionLabel = "Validar e-mail",
                ActionUrl = link,
                Note = "O link expira em 10 minutos. Se você não pediu, ignore este e-mail."
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pre-registration email failed for {Email}.", email);
            throw new ValidationException("email", "Não foi possível enviar o e-mail de validação. Tente de novo.");
        }
    }

    private async Task ApplyTrainingFlagAsync(User user, CancellationToken ct)
    {
        if (await IsTrainingStudentAsync(user.Email, ct))
            user.SetTrainingStudent(true);
    }

    private async Task<bool> IsTrainingStudentAsync(string email, CancellationToken ct)
        => _hotmartOptions.ProductId > 0
           && await _hotmart.HasApprovedAsync(email, _hotmartOptions.ProductId, ct);

    private static string NewRawToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private async Task<AuthResponse> PersistSessionAsync(User user, string action, bool notifyWelcome, CancellationToken ct)
    {
        var pair = _tokens.Issue(user.Id, user.Email);
        _refreshTokens.Add(RefreshToken.Create(_tokens.HashRefreshToken(pair.RefreshToken), DateTime.UtcNow.AddDays(14)));
        AuditRecorder.Record(_audits, _correlation, "User", user.Id, action, new { user.Email, ip = _correlation.ClientIp });
        await _uow.SaveChangesAsync(ct);

        if (notifyWelcome)
        {
            try
            {
                await _email.SendAsync(user.Email, "Bem-vindo ao Conora", new EmailContent(
                    $"Bem-vindo ao Conora, {user.Name}.",
                    ["A organização da sua vida financeira começa aqui. Para aproveitar ao máximo, comece por estes três passos:"])
                {
                    Preheader = "Bem-vindo ao Conora. A organização da sua vida financeira começa aqui.",
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
                _logger.LogWarning(ex, "Welcome email failed for {Email}; account created anyway.", user.Email);
            }
        }

        return new AuthResponse(user.Id, user.Email, pair.AccessToken, pair.RefreshToken, pair.AccessExpiresAtUtc);
    }

    private string AuthLink(string page, string token)
        => $"{PublicWebBase()}/app/{page}?token={token}&app=1";

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
