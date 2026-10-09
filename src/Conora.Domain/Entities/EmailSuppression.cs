namespace Conora.Domain.Entities;

/// <summary>Global blocklist for addresses that bounced or complained (SES feedback).</summary>
public class EmailSuppression
{
    public string Email { get; set; } = default!;
    public string Reason { get; set; } = default!;
    public string? SourceMessageId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public static EmailSuppression Create(string email, string reason, string? sourceMessageId)
    {
        var normalized = Normalize(email);
        if (string.IsNullOrEmpty(normalized))
            throw new ArgumentException("E-mail inválido para supressão.", nameof(email));

        return new EmailSuppression
        {
            Email = normalized,
            Reason = reason.Trim(),
            SourceMessageId = sourceMessageId,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
