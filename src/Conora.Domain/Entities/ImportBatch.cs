using Conora.Domain.Enums;

namespace Conora.Domain.Entities;

public class ImportBatch : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string FileName { get; private set; } = default!;
    public ImportFormat Format { get; private set; }
    public ImportStatus Status { get; private set; }
    public int RowCount { get; private set; }

    private ImportBatch()
    {
    }

    public static ImportBatch Create(string fileName, ImportFormat format, int rowCount) => new()
    {
        FileName = string.IsNullOrWhiteSpace(fileName) ? "import" : fileName.Trim(),
        Format = format,
        Status = ImportStatus.Preview,
        RowCount = rowCount
    };

    public void Commit() => Status = ImportStatus.Committed;
}
