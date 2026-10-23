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
    private readonly IStoreReceiptValidator _receipts;
    private readonly IFamilyGroupRepository _users;
    private readonly StoreBillingOptions _store;
    private readonly ILogger<StoreSubscriptionBilling> _logger;

    public StoreSubscriptionBilling(
        PlanService plans,
        IStoreReceiptValidator receipts,
        IFamilyGroupRepository users,
        IOptions<StoreBillingOptions> store,
        ILogger<StoreSubscriptionBilling> logger)
    {
        _plans = plans;
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

        if (!string.Equals(receipt.ProductId, command.ProductId.Trim(), StringComparison.Ordinal))
            throw new ValidationException("productId", "O produto confirmado pela loja não confere.");
        if (receipt.AccountId.HasValue && receipt.AccountId.Value != command.UsuarioId)
            throw new ValidationException("store", "Esta compra está presa a outro login.");
        if (!TryMapProduct(receipt.ProductId, out var plan))
            throw new ValidationException("productId", "Produto da loja não reconhecido.");
        if (!receipt.Entitled)
            throw new ValidationException("store", "A loja ainda não confirmou o pagamento desta assinatura.");

        var storeLabel = StoreLabel(command.Channel);
        var linked = await _users.FindSubscriptionByStoreIdAsync(storeLabel, receipt.SubscriptionId, ct);
        if (linked is not null && linked.UsuarioId != command.UsuarioId)
            throw new ValidationException("store", "Esta compra está presa a outro login.");

        await _plans.ApplyStoreConfirmationAsync(ToConfirmation(command.UsuarioId, plan, storeLabel, receipt), ct);
        _logger.LogInformation(
            "Store purchase confirmed user={UserId} product={ProductId} plan={Plan} channel={Channel} ref={Ref} expires={ExpiresAt}",
            command.UsuarioId,
            receipt.ProductId,
            plan,
            command.Channel,
            ExternalReference(command.Channel, receipt.TransactionId),
            receipt.ExpiresAt);
    }

    public async Task ApplyNoticeAsync(StoreNoticeCommand command, CancellationToken ct = default)
    {
        var receipt = command.Receipt;
        var storeLabel = StoreLabel(command.Channel);
        if (!TryMapProduct(receipt.ProductId, out var plan))
        {
            _logger.LogWarning("Store notice {Event} ignored: unknown product {ProductId}", command.Evento, receipt.ProductId);
            return;
        }

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

    private static StorePurchaseConfirmation ToConfirmation(Guid usuarioId, PlanKind plan, string store, StoreReceipt receipt) =>
        new(usuarioId, plan, store, receipt.ProductId, receipt.SubscriptionId, receipt.ExpiresAt, receipt.Entitled);

    private static bool IdsEqual(string a, string b) =>
        string.Equals(a, b, StringComparison.Ordinal);
}
