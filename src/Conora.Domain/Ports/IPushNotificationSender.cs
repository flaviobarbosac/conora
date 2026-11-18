namespace Conora.Domain.Ports;

public sealed record PushMessage(
    string Title,
    string Body,
    IReadOnlyDictionary<string, string>? Data = null);

public sealed record PushDeliveryResult(
    int Attempted,
    int Succeeded,
    int Failed,
    IReadOnlyList<string> InvalidTokens);

public interface IPushNotificationSender
{
    /// <summary>False when Fcm credentials/project are missing; send becomes a no-op.</summary>
    bool IsConfigured { get; }

    Task<PushDeliveryResult> SendAsync(
        IReadOnlyList<string> tokens,
        PushMessage message,
        CancellationToken ct);
}
