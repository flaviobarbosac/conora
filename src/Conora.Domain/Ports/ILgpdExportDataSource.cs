namespace Conora.Domain.Ports;

/// <summary>Loads every user-owned entity set for the LGPD Excel workbook.</summary>
public interface ILgpdExportDataSource
{
    Task<IReadOnlyList<(string SheetName, Type EntityType, IReadOnlyList<object> Rows)>> LoadAsync(
        Guid usuarioId,
        string userEmail,
        CancellationToken ct = default);
}
