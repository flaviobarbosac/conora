namespace Conora.Domain.Ports;

public interface IWhatsAppMessenger
{
    bool IsConfigured { get; }

    Task SendTextAsync(string phoneE164, string text, CancellationToken ct);
}
