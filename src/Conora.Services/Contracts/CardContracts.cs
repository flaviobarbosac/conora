using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record CreateCardRequest(string Name, decimal LimitTotal, int ClosingDay, int DueDay, Guid? PaymentAccountId = null);

public sealed record CardResponse(
    Guid Id,
    string Name,
    decimal LimitTotal,
    int ClosingDay,
    int DueDay,
    Guid? PaymentAccountId,
    decimal UsedLimit,
    decimal AvailableLimit);

public sealed record CardPurchaseRequest(
    decimal Amount, DateTime PurchasedAt, int Installments, Guid ChartAccountId, string Description);

public sealed record CardPurchaseResponse(
    Guid Id,
    Guid CreditCardId,
    decimal Amount,
    DateTime PurchasedAt,
    int Installments,
    Guid ChartAccountId,
    string Description,
    string CompetenceYm,
    string FirstInvoiceYm);

public sealed record CardInvoiceResponse(
    Guid CreditCardId, string CompetenceYm, InvoiceStatus Status, decimal Total, DateOnly ClosingDate, DateOnly DueDate);

public sealed record PayInvoiceRequest(Guid? AccountId = null, DateTime? PaidAt = null);
