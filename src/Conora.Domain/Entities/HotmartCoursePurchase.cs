namespace Conora.Domain.Entities;

/// <summary>
/// Hotmart purchase of the training product. Access (90-day trial) starts on the 8th day
/// after the purchase, and only for PURCHASE_COMPLETE. Cancel and refund request revoke it.
/// </summary>
public class HotmartCoursePurchase
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Email { get; set; } = default!;
    public string Transaction { get; set; } = default!;
    public long? ProductId { get; set; }
    /// <summary>True only after the 8th-day release. Registration reads this flag.</summary>
    public bool Approved { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime AcquiredAt { get; set; }
    /// <summary>Morning of the 8th day in Brazil, counting the purchase day as day 1.</summary>
    public DateTime? UnlockAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? ReleaseEmailSentAt { get; set; }

    public bool IsRevoked => RevokedAt is not null;

    public static bool CompletesPurchase(string? eventName) => eventName == "PURCHASE_COMPLETE";

    public static bool RevokesAccess(string? eventName) =>
        eventName is "PURCHASE_CANCELED" or "PURCHASE_PROTEST";

    /// <summary>Day 1 is the purchase calendar day in Brazil. Release is 09:00 on day 8.</summary>
    public static DateTime EighthDayMorningUtc(DateTime acquiredUtc)
    {
        var tz = BrazilTimeZone();
        var acquired = DateTime.SpecifyKind(acquiredUtc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(acquired, tz);
        var eighthMorning = local.Date.AddDays(7).AddHours(9);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(eighthMorning, DateTimeKind.Unspecified), tz);
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
            UnlockAt = EighthDayMorningUtc(acquired)
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
        Approved = false;
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
