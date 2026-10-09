namespace Conora.Domain.Entities;

public class ImportPreviewRow : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public Guid BatchId { get; private set; }
    public string RawJson { get; private set; } = default!;
    public decimal MappedAmount { get; private set; }
    public DateTime MappedDate { get; private set; }
    public string MappedDescription { get; private set; } = default!;
    public string ImportHash { get; private set; } = default!;
    public bool IsDuplicate { get; private set; }
    public bool WillImport { get; private set; }

    private ImportPreviewRow()
    {
    }

    public static ImportPreviewRow Create(
        Guid batchId,
        string rawJson,
        decimal mappedAmount,
        DateTime mappedDate,
        string mappedDescription,
        string importHash,
        bool isDuplicate) => new()
    {
        BatchId = batchId,
        RawJson = rawJson,
        MappedAmount = mappedAmount,
        MappedDate = mappedDate,
        MappedDescription = mappedDescription,
        ImportHash = importHash,
        IsDuplicate = isDuplicate,
        WillImport = !isDuplicate
    };

    public void SetWillImport(bool willImport) => WillImport = willImport;
}
