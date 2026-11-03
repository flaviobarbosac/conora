using System.Security.Cryptography;
using System.Text;
using Conora.Domain.Catalog;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Infrastructure.Billing;
using Conora.Repository.Interface;
using Conora.Services.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conora.Services.Billing;

/// <summary>
/// Conora adaptation of Clampfy StoreAssinaturaService: validates the purchase with the store,
/// maps product → PlanKind and mirrors the store state (expiry, refund, expiration) on the plan.
/// Free-trial length (90 student / 30 others) is NOT a store SKU — AccessPolicy uses IsTrainingStudent.
/// </summary>
public sealed class StoreSubscriptionBilling : IStoreSubscriptionBilling
{
    private const int MaxExternalRef = 120;

    private readonly PlanService _plans;
    private readonly FamilyGroupService _succession;
    private readonly IStoreReceiptValidator _receipts;
    private readonly IFamilyGroupRepository _users;
    private readonly StoreBillingOptions _store;
    private readonly ILogger<StoreSubscriptionBilling> _logger;

    public StoreSubscriptionBilling(
        PlanService plans,
        FamilyGroupService succession,
        IStoreReceiptValidator receipts,
        IFamilyGroupRepository users,
        IOptions<StoreBillingOptions> store,
        ILogger<StoreSubscriptionBilling> logger)
    {
        _plans = plans;
        _succession = succession;
        _receipts = receipts;
        _users = users;
        _store = store.Value;
        _logger = logger;
    }

    public async Task ConfirmAsync(StorePurchaseCommand command, CancellationToken ct = default)
    {
        if (command.Channel is not (StoreBillingChannel.Google or StoreBillingChannel.Apple))
            throw new ValidationException("store", "Canal de loja inválido.");
        if (string.IsNullOrWhiteSpace(command.ProductId) || string.IsNullOrWhiteSpace(command.TransactionId))
            throw new ValidationException("store", "Recibo da loja incompleto.");
        if (command.Channel == StoreBillingChannel.Google && string.IsNullOrWhiteSpace(command.PurchaseToken))
            throw new ValidationException("store", "Token da loja obrigatório.");

        StoreReceipt receipt;
        try
        {
            receipt = await _receipts.ValidateAsync(
                command.Channel,
                command.ProductId.Trim(),
                command.TransactionId.Trim(),
                command.PurchaseToken?.Trim() ?? "",
                ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Store validation failed user={UserId} product={ProductId}", command.UsuarioId, command.ProductId);
            throw new ValidationException("store", ex.Message);
        }

        var requested = command.ProductId.Trim();
        var storeLabel = StoreLabel(command.Channel);
        if (!string.Equals(receipt.ProductId, requested, StringComparison.Ordinal))
            _logger.LogInformation(
                "Store returned product {StoreProductId} renewal {RenewalProductId} renewing {WillRenew} for requested {RequestedProductId} user={UserId}",
                receipt.ProductId, receipt.RenewalProductId, receipt.WillRenew, requested, command.UsuarioId);

        // A cancel (auto-renew off) or an expired period ends the plan already linked to this login.
        // It must not activate a different product the Apple ID still has from an older purchase.
        if (!receipt.Entitled)
        {
            var current = await _users.FindSubscriptionByStoreIdAsync(storeLabel, receipt.SubscriptionId, ct);
            if (current is not null && (current.UsuarioId == command.UsuarioId || receipt.Sandbox))
            {
                if (current.UsuarioId != command.UsuarioId)
                    await _users.ReleaseStoreSubscriptionAsync(current.UsuarioId, storeLabel, receipt.SubscriptionId, DateTime.UtcNow, ct);
                if (current.UsuarioId == command.UsuarioId)
                {
                    var endingProduct = current.StoreProductId ?? receipt.ProductId;
                    if (!TryMapProduct(endingProduct, out var ending))
                        ending = current.Plan;
                    await _plans.ApplyStoreConfirmationAsync(ToConfirmation(command.UsuarioId, ending, storeLabel, receipt), ct);
                }
            }
            else if (current is null)
                throw new ValidationException("store", "A loja ainda não confirmou o pagamento desta assinatura.");
            return;
        }

        var granted = GrantedProduct(receipt, requested);
        if (!string.Equals(granted, requested, StringComparison.Ordinal))
            throw new ValidationException(
                "store",
                "A App Store ainda tem outro plano nesta conta da Apple. Cancele essa assinatura na loja antes de assinar este.");
        if (!TryMapProduct(granted, out var plan))
            throw new ValidationException("productId", "Produto da loja não reconhecido.");

        // Real purchases stay bound to the first login. Test purchases (TestFlight, App Review, license
        // testers) share one store account across many logins, so they move to whoever confirms last.
        if (!receipt.Sandbox && receipt.AccountId.HasValue && receipt.AccountId.Value != command.UsuarioId)
            throw new ValidationException("store", "Esta compra está presa a outro login.");

        var linked = await _users.FindSubscriptionByStoreIdAsync(storeLabel, receipt.SubscriptionId, ct);
        if (linked is not null && linked.UsuarioId != command.UsuarioId)
        {
            if (!receipt.Sandbox)
                throw new ValidationException("store", "Esta compra está presa a outro login.");
            await _users.ReleaseStoreSubscriptionAsync(linked.UsuarioId, storeLabel, receipt.SubscriptionId, DateTime.UtcNow, ct);
            _logger.LogInformation(
                "Sandbox store subscription moved from user={PreviousUserId} to user={UserId} channel={Channel}",
                linked.UsuarioId, command.UsuarioId, command.Channel);
        }

        await _plans.ApplyStoreConfirmationAsync(ToConfirmation(command.UsuarioId, plan, storeLabel, receipt with { ProductId = granted }), ct);
        _logger.LogInformation(
            "Store purchase confirmed user={UserId} product={ProductId} plan={Plan} channel={Channel} ref={Ref} expires={ExpiresAt}",
            command.UsuarioId,
            receipt.ProductId,
            plan,
            command.Channel,
            ExternalReference(command.Channel, receipt.TransactionId),
            receipt.ExpiresAt);
    }

    public async Task RefreshAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var sub = await _users.FindSubscriptionAsync(usuarioId, ct);
        if (sub is null || sub.DeletedAt is not null)
            return;
        if (!string.Equals(sub.Store, "app-store", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(sub.StoreSubscriptionId))
            return;

        StoreReceipt receipt;
        try
        {
            receipt = await _receipts.FetchAppleSubscriptionAsync(sub.StoreSubscriptionId, ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "App Store refresh failed user={UserId}", usuarioId);
            return;
        }

        var product = receipt.Entitled ? GrantedProduct(receipt, receipt.RenewalProductId ?? receipt.ProductId) : (sub.StoreProductId ?? receipt.ProductId);
        if (!TryMapProduct(product, out var plan))
            plan = sub.Plan;
        await _plans.ApplyStoreConfirmationAsync(
            ToConfirmation(usuarioId, plan, "app-store", receipt with { ProductId = product }), ct);
        if (!receipt.Entitled)
            await _succession.MoveBudgetsOnStoreCancellationAsync(usuarioId, ct);
    }

    public async Task ApplyNoticeAsync(StoreNoticeCommand command, CancellationToken ct = default)
    {
        var receipt = command.Receipt;
        var storeLabel = StoreLabel(command.Channel);
        var product = receipt.Entitled
            ? GrantedProduct(receipt, receipt.RenewalProductId ?? receipt.ProductId)
            : receipt.ProductId;
        if (!TryMapProduct(product, out var plan))
        {
            _logger.LogWarning("Store notice {Event} ignored: unknown product {ProductId}", command.Evento, product);
            return;
        }
        receipt = receipt with { ProductId = product };

        // The store subscription id wins over the account token: renewals keep the original owner.
        var linked = await _users.FindSubscriptionByStoreIdAsync(storeLabel, receipt.SubscriptionId, ct);
        var usuarioId = linked?.UsuarioId ?? receipt.AccountId;
        if (usuarioId is null)
        {
            _logger.LogWarning(
                "Store notice {Event} ignored: no Conora login for {Store} subscription (product {ProductId})",
                command.Evento, storeLabel, receipt.ProductId);
            return;
        }
        if (linked is null && !receipt.Entitled)
        {
            _logger.LogInformation("Store notice {Event} for an unlinked inactive subscription; nothing to end.", command.Evento);
            return;
        }

        var user = await _users.FindUserByIdAsync(usuarioId.Value, ct);
        if (user is null || user.DeletedAt is not null)
        {
            _logger.LogWarning("Store notice {Event} ignored: user {UserId} not found", command.Evento, usuarioId);
            return;
        }

        await _plans.ApplyStoreConfirmationAsync(ToConfirmation(usuarioId.Value, plan, storeLabel, receipt), ct);
        if (!receipt.Entitled)
            await _succession.MoveBudgetsOnStoreCancellationAsync(usuarioId.Value, ct);
        _logger.LogInformation(
            "Store notice {Event} applied user={UserId} product={ProductId} entitled={Entitled} expires={ExpiresAt}",
            command.Evento, usuarioId, receipt.ProductId, receipt.Entitled, receipt.ExpiresAt);
    }

    public bool TryMapProduct(string productId, out PlanKind plan)
    {
        plan = default;
        if (string.IsNullOrWhiteSpace(productId))
            return false;

        var id = productId.Trim();
        if (IdsEqual(id, _store.IndividualMonthlyProductId)) { plan = PlanKind.IndividualMonthly; return true; }
        if (IdsEqual(id, _store.IndividualYearlyProductId)) { plan = PlanKind.IndividualYearly; return true; }
        if (IdsEqual(id, _store.DuoMonthlyProductId)) { plan = PlanKind.FamilyMonthly; return true; }
        if (IdsEqual(id, _store.DuoYearlyProductId)) { plan = PlanKind.FamilyYearly; return true; }
        if (IdsEqual(id, _store.FamilyMonthlyProductId)) { plan = PlanKind.FamilyPlusMonthly; return true; }
        if (IdsEqual(id, _store.FamilyYearlyProductId)) { plan = PlanKind.FamilyPlusYearly; return true; }
        return false;
    }

    public decimal? PriceOfProduct(string productId) =>
        TryMapProduct(productId, out var plan) ? PlanCatalog.PriceOf(plan) : null;

    public static string StoreLabel(StoreBillingChannel channel) =>
        channel == StoreBillingChannel.Google ? "google-play" : "app-store";

    public static string ExternalReference(StoreBillingChannel channel, string transactionId)
    {
        var reference = $"loja:{(int)channel}:{transactionId}";
        if (reference.Length <= MaxExternalRef)
            return reference;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transactionId)));
        return $"loja:{(int)channel}:sha256:{hash}";
    }

    /// <summary>
    /// The product the user just chose wins when Apple scheduled it as the next renewal.
    /// Otherwise the current store product is used, and a mismatch is refused by the caller.
    /// </summary>
    private static string GrantedProduct(StoreReceipt receipt, string requested)
    {
        if (!string.IsNullOrWhiteSpace(receipt.RenewalProductId)
            && string.Equals(receipt.RenewalProductId, requested, StringComparison.Ordinal))
            return receipt.RenewalProductId;
        return receipt.ProductId;
    }

    private static StorePurchaseConfirmation ToConfirmation(Guid usuarioId, PlanKind plan, string store, StoreReceipt receipt) =>
        new(usuarioId, plan, store, receipt.ProductId, receipt.SubscriptionId, receipt.ExpiresAt, receipt.Entitled);

    private static bool IdsEqual(string a, string b) =>
        string.Equals(a, b, StringComparison.Ordinal);
}
