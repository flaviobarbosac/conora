namespace Conora.Domain.Ports;

public interface IEmailSender
{
    /// <summary>Sends a branded e-mail (HTML layout with a plain-text alternative).</summary>
    Task SendAsync(string to, string subject, EmailContent content, CancellationToken ct = default);
}

/// <summary>
/// Structured e-mail body rendered into the standard layout. All text is plain text;
/// the renderer is responsible for HTML encoding.
/// </summary>
public sealed record EmailContent(string Title, IReadOnlyList<string> Paragraphs)
{
    /// <summary>Hidden preview text shown by mail clients next to the subject.</summary>
    public string? Preheader { get; init; }

    /// <summary>Short line under the title in the dark header.</summary>
    public string? Subtitle { get; init; }

    public IReadOnlyList<EmailStep> Steps { get; init; } = [];

    public string? ActionLabel { get; init; }

    public string? ActionUrl { get; init; }

    /// <summary>Highlighted note below the call-to-action (e.g. expiration info).</summary>
    public string? Note { get; init; }
}

public sealed record EmailStep(string Title, string Description);
