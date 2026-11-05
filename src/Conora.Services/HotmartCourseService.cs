using Conora.Domain.Entities;
using Conora.Domain.Ports;
using Conora.Infrastructure.Billing;
using Conora.Repository.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conora.Services;

/// <summary>
/// PURCHASE_COMPLETE schedules the 90-day student trial for the 8th day after the purchase.
/// PURCHASE_CANCELED and PURCHASE_PROTEST remove that access. The release e-mail goes out on day 8.
/// </summary>
public sealed class HotmartCourseService
{
    private const string PlayStoreUrl = "https://play.google.com/store/apps/details?id=br.com.conora.app";
    private const string AppStoreUrl = "https://apps.apple.com/br/app/id6825791102";

    private readonly IHotmartCourseStore _purchases;
    private readonly IUserRepository _users;
    private readonly IPreRegistrationRepository _preRegistrations;
    private readonly IUnitOfWork _uow;
    private readonly IEmailSender _email;
    private readonly ILogger<HotmartCourseService> _logger;
    private readonly HotmartOptions _options;

    public HotmartCourseService(
        IHotmartCourseStore purchases,
        IUserRepository users,
        IPreRegistrationRepository preRegistrations,
        IUnitOfWork uow,
        IEmailSender email,
        ILogger<HotmartCourseService> logger,
        IOptions<HotmartOptions> options)
    {
        _purchases = purchases;
        _users = users;
        _preRegistrations = preRegistrations;
        _uow = uow;
        _email = email;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<bool> ReceiveAsync(
        string? eventName,
        string? email,
        string? transaction,
        long? productId,
        DateTime? acquiredAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;
        if (_options.ProductId <= 0 || productId != _options.ProductId)
            return false;

        var normalized = email.Trim().ToLowerInvariant();
        if (HotmartCoursePurchase.RevokesAccess(eventName))
            return await RevokeAsync(normalized, transaction, ct);

        if (!HotmartCoursePurchase.CompletesPurchase(eventName) || string.IsNullOrWhiteSpace(transaction))
            return false;

        var tx = transaction.Trim();
        var existing = await _purchases.FindByTransactionAsync(tx, ct);
        if (existing is null)
        {
            var acquired = acquiredAtUtc ?? DateTime.UtcNow;
            existing = HotmartCoursePurchase.Schedule(normalized, tx, productId, acquired);
            await _purchases.AddAsync(existing, ct);
        }
        else if (existing.IsRevoked)
        {
            return false;
        }

        if (existing.UnlockAt is not null && existing.UnlockAt <= DateTime.UtcNow)
            await ReleaseOneAsync(existing, ct);

        await _uow.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Grants access and sends the store e-mail for purchases whose 8th day has arrived.</summary>
    public async Task<int> ReleaseDueAsync(CancellationToken ct)
    {
        var due = await _purchases.ListAwaitingReleaseAsync(DateTime.UtcNow, ct);
        var released = 0;
        foreach (var purchase in due)
        {
            if (await ReleaseOneAsync(purchase, ct))
                released++;
        }

        if (due.Count > 0)
            await _uow.SaveChangesAsync(ct);
        return released;
    }

    private async Task<bool> RevokeAsync(string email, string? transaction, CancellationToken ct)
    {
        var rows = await _purchases.ListByEmailAsync(email, _options.ProductId, ct);
        var targets = string.IsNullOrWhiteSpace(transaction)
            ? rows.Where(p => !p.IsRevoked).ToList()
            : rows.Where(p => p.Transaction == transaction.Trim() && !p.IsRevoked).ToList();
        if (targets.Count == 0)
            return false;

        foreach (var purchase in targets)
            purchase.Revoke();

        await SyncStudentFlagAsync(email, null, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> ReleaseOneAsync(HotmartCoursePurchase purchase, CancellationToken ct)
    {
        if (purchase.IsRevoked || purchase.UnlockAt is null || purchase.UnlockAt > DateTime.UtcNow)
            return false;

        if (!purchase.Approved)
            purchase.GrantAccess();

        await SyncStudentFlagAsync(purchase.Email, purchase, ct);

        if (purchase.ReleaseEmailSentAt is not null)
            return true;

        try
        {
            var name = await DisplayNameAsync(purchase.Email, ct);
            await _email.SendAsync(purchase.Email, "Seu Conora está liberado", ReleaseEmail(name), ct);
            purchase.MarkReleaseEmailSent();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hotmart release email failed for {Email}.", purchase.Email);
        }

        return true;
    }

    private async Task SyncStudentFlagAsync(string email, HotmartCoursePurchase? current, CancellationToken ct)
    {
        var rows = await _purchases.ListByEmailAsync(email, _options.ProductId, ct);
        var granted = rows.Any(p => p.Approved && !p.IsRevoked)
            || (current is not null && current.Email == email && current.Approved && !current.IsRevoked);

        var user = await _users.FindByEmailAsync(email, ct);
        if (user is not null)
            user.SetTrainingStudent(granted);

        var pending = await _preRegistrations.FindByEmailAsync(email, ct);
        if (pending is null)
            return;
        if (granted)
            pending.MarkTrainingStudent();
        else
            pending.ClearTrainingStudent();
    }

    private async Task<string> DisplayNameAsync(string email, CancellationToken ct)
    {
        var user = await _users.FindByEmailAsync(email, ct);
        if (!string.IsNullOrWhiteSpace(user?.Name) && !string.Equals(user.Name, email, StringComparison.OrdinalIgnoreCase))
            return user.Name.Trim();
        var pending = await _preRegistrations.FindByEmailAsync(email, ct);
        if (!string.IsNullOrWhiteSpace(pending?.Name) && !string.Equals(pending.Name, email, StringComparison.OrdinalIgnoreCase))
            return pending.Name.Trim();
        return "olá";
    }

    private static EmailContent ReleaseEmail(string name) => new(
        $"{name}, o Conora está liberado para você.",
        [
            "Oito dias depois da sua compra na Hotmart, o aplicativo está pronto para baixar.",
            "Entre com o mesmo e-mail da compra. Sua conta de aluno abre com 90 dias no Conora.",
            "Vivendo bem o presente, planejando um futuro melhor."
        ])
    {
        Preheader = "Seu Conora de aluno está liberado na App Store e no Google Play.",
        Subtitle = "Vivendo bem o presente, planejando um futuro melhor.",
        Steps =
        [
            new EmailStep("Baixe o aplicativo", "App Store no iPhone ou Google Play no Android."),
            new EmailStep("Entre com este e-mail", "É o mesmo endereço que você usou na Hotmart."),
            new EmailStep("Comece pelo hoje", "Registre o que entrou e o que saiu. O resto do mês se organiza em cima disso.")
        ],
        Links =
        [
            new EmailLink("Baixar na App Store", AppStoreUrl),
            new EmailLink("Baixar no Google Play", PlayStoreUrl)
        ],
        Note = "O acesso de aluno vale 90 dias a partir da sua conta no Conora."
    };

    public Task<HotmartStudentPage> ListStudentsAsync(
        string? email,
        string? name,
        string? status,
        DateTime? acquiredFrom,
        DateTime? acquiredTo,
        DateTime? releasedFrom,
        DateTime? releasedTo,
        int page,
        int pageSize,
        CancellationToken ct = default)
        => _purchases.ListStudentsAsync(
            new HotmartStudentQuery(
                _options.ProductId,
                email,
                name,
                status,
                acquiredFrom,
                acquiredTo,
                releasedFrom,
                releasedTo,
                page,
                pageSize),
            ct);
}
