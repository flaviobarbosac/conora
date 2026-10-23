using System.Security.Cryptography;
using System.Text;
using Conora.Domain.Catalog;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Infrastructure.Billing;
using Conora.Repository.Interface;
using Conora.Services.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conora.Services.Billing;

/// <summary>
/// Conora adaptation of Clampfy StoreAssinaturaService: validates receipt, maps product→PlanKind,
/// activates via PlanService, applies store notices.
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

        if (string.IsNullOrWhiteSpace(command.PurchaseToken))
            throw new ValidationException("store", "Token da loja obrigatório.");

        var receipt = await _receipts.ValidateAsync(
            command.Channel,
            command.ProductId,
            command.TransactionId,
            command.PurchaseToken,
            ct);

        if (!string.Equals(receipt.ProductId, command.ProductId.Trim(), StringComparison.Ordinal))
            throw new ValidationException("productId", "O produto confirmado pela loja não confere.");

        if (receipt.AccountId.HasValue && receipt.AccountId.Value != command.UsuarioId)
            throw new ValidationException("store", "Esta compra está presa a outro login.");

        if (!TryMapProduct(receipt.ProductId, out var plan))
            throw new ValidationException("productId", "Produto da loja não reconhecido.");

        var user = await _users.FindUserByIdAsync(command.UsuarioId, ct);
        var trialDays = AccessPolicy.TrialDaysFor(user?.IsTrainingStudent ?? true);
        _logger.LogInformation(
            "Store confirm user={UserId} product={ProductId} plan={Plan} trialProfileDays={TrialDays} student={Student}",
            command.UsuarioId,
            receipt.ProductId,
            plan,
            trialDays,
            user?.IsTrainingStudent ?? true);

        if (receipt.InTrial || !receipt.Paid)
        {
            _logger.LogInformation(
                "Store intro/trial receipt for user={UserId}; paid plan not activated yet.",
                command.UsuarioId);
            return;
        }

        var storeLabel = command.Channel == StoreBillingChannel.Google ? "google-play" : "app-store";
        await _plans.ApplyStoreConfirmationAsync(
            new StorePurchaseConfirmation(command.UsuarioId, plan, command.PurchaseToken.Trim(), storeLabel),
            ct);

        _logger.LogInformation(
            "Store purchase confirmed user={UserId} product={ProductId} plan={Plan} channel={Channel} ref={Ref}",
            command.UsuarioId,
            receipt.ProductId,
            plan,
            command.Channel,
            ExternalReference(command.Channel, receipt.TransactionId));
    }

    public async Task ApplyNoticeAsync(StoreNoticeCommand command, CancellationToken ct = default)
    {
        if (command.UsuarioId is null)
        {
            _logger.LogWarning("Store notice ignored: missing usuarioId event={Event}", command.Evento);
            return;
        }

        var evento = command.Evento.Trim().ToLowerInvariant();
        if (evento is "pago" or "renovado" or "purchased" or "renewed" or "didrenew")
        {
            if (!TryMapProduct(command.ProductId, out var plan))
            {
                _logger.LogWarning("Store notice product unknown: {ProductId}", command.ProductId);
                return;
            }

            var user = await _users.FindUserByIdAsync(command.UsuarioId.Value, ct);
            _logger.LogInformation(
                "Store notice {Event} user={UserId} product={ProductId} trialProfileDays={Days} student={Student}",
                evento,
                command.UsuarioId,
                command.ProductId,
                AccessPolicy.TrialDaysFor(user?.IsTrainingStudent ?? true),
                user?.IsTrainingStudent ?? true);

            if (command.EmTrial)
            {
                _logger.LogInformation(
                    "Store trial notice for user={UserId} product={ProductId}; plan not activated until paid.",
                    command.UsuarioId, command.ProductId);
                return;
            }

            var token = string.IsNullOrWhiteSpace(command.SubscriptionId)
                ? command.TransactionId
                : command.SubscriptionId!;
            var storeLabel = command.Channel == StoreBillingChannel.Google ? "google-play" : "app-store";
            await _plans.ApplyStoreConfirmationAsync(
                new StorePurchaseConfirmation(command.UsuarioId.Value, plan, token, storeLabel),
                ct);
            return;
        }

        if (evento is "expirado" or "expired" or "reembolso" or "refund" or "revoke" or "didfailtorenew")
        {
            _logger.LogInformation(
                "Store notice {Event} user={UserId} product={ProductId} — acknowledged (cancel API stub).",
                evento, command.UsuarioId, command.ProductId);
        }
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

    public static string ExternalReference(StoreBillingChannel channel, string transactionId)
    {
        var reference = $"loja:{(int)channel}:{transactionId}";
        if (reference.Length <= MaxExternalRef)
            return reference;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transactionId)));
        return $"loja:{(int)channel}:sha256:{hash}";
    }

    private static bool IdsEqual(string a, string b) =>
        string.Equals(a, b, StringComparison.Ordinal);
}
