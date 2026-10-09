namespace Conora.Domain.Ports;

public interface IEmailSuppressionStore
{
    Task<bool> IsSuppressedAsync(string email, CancellationToken ct = default);
    Task UpsertAsync(string email, string reason, string? sourceMessageId, CancellationToken ct = default);
}
