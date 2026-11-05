namespace Conora.Domain.Entities;

/// <summary>
/// Hotmart purchase of the training product. Login stays closed for 98 days after
/// <see cref="AcquiredAt"/>. The 90-day student trial starts when that date arrives,
/// and only for PURCHASE_COMPLETE. Cancel and refund request set <see cref="RevokedAt"/>.
/// There is no status column: revoked, pending, active and expired are derived from
/// <see cref="Approved"/>, <see cref="RevokedAt"/> and <see cref="UnlockAt"/>.
/// </summary>
public class HotmartCoursePurchase
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Email { get; set; } = default!;
    public string Transaction { get; set; } = default!;
    public long? ProductId { get; set; }
    /// <summary>True only after the 98-day release. Registration reads this flag.</summary>
    public bool Approved { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime AcquiredAt { get; set; }
    /// <summary>Instant the student may sign in: <see cref="AcquiredAt"/> plus <see cref="AccessWaitingDays"/>.</summary>
    public DateTime? UnlockAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? ReleaseEmailSentAt { get; set; }

    public const int ExportGraceDays = 2;

    /// <summary>Days after the Hotmart purchase before the student can sign in.</summary>
    public const int AccessWaitingDays = 98;

    public bool IsRevoked => RevokedAt is not null;

    /// <summary>Two days after a granted purchase is cancelled. Only export is allowed in this window.</summary>
    public DateTime? ExportUntilUtc =>
        Approved && RevokedAt is DateTime revoked ? revoked.AddDays(ExportGraceDays) : null;

    public static bool CompletesPurchase(string? eventName) => eventName == "PURCHASE_COMPLETE";

    public static bool RevokesAccess(string? eventName) =>
        eventName is "PURCHASE_CANCELED" or "PURCHASE_PROTEST";

    /// <summary>Exact instant access opens: the purchase timestamp plus 98 days.</summary>
    public static DateTime AccessAvailableAtUtc(DateTime acquiredUtc)
        => DateTime.SpecifyKind(acquiredUtc, DateTimeKind.Utc).AddDays(AccessWaitingDays);

    /// <summary>Login message with the Brazil-local date and time access opens.</summary>
    public static string AccessAvailableMessage(DateTime availableUtc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(availableUtc, DateTimeKind.Utc),
            BrazilTimeZone());
        return $"Seu acesso fica disponível a partir de {local:dd/MM/yyyy} às {local:HH:mm}.";
    }

    public static HotmartCoursePurchase Schedule(string email, string transaction, long? productId, DateTime acquiredUtc)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
            throw new ArgumentException("E-mail inválido.", nameof(email));
        if (string.IsNullOrWhiteSpace(transaction))
            throw new ArgumentException("Transação inválida.", nameof(transaction));

        var acquired = DateTime.SpecifyKind(acquiredUtc, DateTimeKind.Utc);
        return new HotmartCoursePurchase
        {
            Email = normalized,
            Transaction = transaction.Trim(),
            ProductId = productId,
            Approved = false,
            ReceivedAt = DateTime.UtcNow,
            AcquiredAt = acquired,
            UnlockAt = AccessAvailableAtUtc(acquired)
        };
    }

    public void GrantAccess()
    {
        if (IsRevoked)
            return;
        Approved = true;
    }

    public void Revoke()
    {
        // Keep Approved so a grant that already happened stays distinguishable from a cancel
        // before the 98 days. Access checks always require RevokedAt == null.
        RevokedAt ??= DateTime.UtcNow;
    }

    public void MarkReleaseEmailSent() => ReleaseEmailSentAt = DateTime.UtcNow;

    private static TimeZoneInfo BrazilTimeZone()
    {
        var tzId = OperatingSystem.IsWindows()
            ? "E. South America Standard Time"
            : "America/Sao_Paulo";
        return TimeZoneInfo.FindSystemTimeZoneById(tzId);
    }
}
