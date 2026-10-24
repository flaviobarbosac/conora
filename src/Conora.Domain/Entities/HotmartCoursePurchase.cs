namespace Conora.Domain.Entities;

/// <summary>
/// Approved Hotmart course purchase. Matching this e-mail marks the Conora account as a training student (90-day trial).
/// </summary>
public class HotmartCoursePurchase
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Email { get; set; } = default!;
    public string Transaction { get; set; } = default!;
    public long? ProductId { get; set; }
    public bool Approved { get; set; }
    public DateTime ReceivedAt { get; set; }

    public static bool GrantsStudentTrial(string? eventName) =>
        eventName is "PURCHASE_APPROVED" or "PURCHASE_COMPLETE";

    public static HotmartCoursePurchase CreateApproved(string email, string transaction, long? productId)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
            throw new ArgumentException("E-mail inválido.", nameof(email));
        if (string.IsNullOrWhiteSpace(transaction))
            throw new ArgumentException("Transação inválida.", nameof(transaction));

        return new HotmartCoursePurchase
        {
            Email = normalized,
            Transaction = transaction.Trim(),
            ProductId = productId,
            Approved = true,
            ReceivedAt = DateTime.UtcNow
        };
    }
}
