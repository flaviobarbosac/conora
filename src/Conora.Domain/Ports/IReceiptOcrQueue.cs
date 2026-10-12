namespace Conora.Domain.Ports;

public interface IReceiptOcrQueue
{
    Task<IReadOnlyList<(Guid Id, Guid UsuarioId)>> ListProcessingAsync(int take, CancellationToken ct = default);

    Task<IReadOnlyList<(Guid Id, Guid UsuarioId, string ImageObjectKey)>> ListExpiredImagesAsync(
        int take,
        CancellationToken ct = default);
}
