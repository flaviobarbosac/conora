namespace Conora.Domain.Ports;

public interface IEmailSuppressionStore
{
    Task<bool> IsSuppressedAsync(string email, CancellationToken ct = default);
    Task UpsertAsync(string email, string reason, string? sourceMessageId, CancellationToken ct = default);
    /// <summary>Removes suppression only when the reason is user opt-out (keeps SES bounce/complaint).</summary>
    Task RemoveUserOptOutAsync(string email, CancellationToken ct = default);
}
