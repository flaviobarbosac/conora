using Conora.Domain.Enums;
using Conora.Domain.Exceptions;

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
    public ImportRowStatus RowStatus { get; private set; }
    public Guid? SuggestedCategoryId { get; private set; }
    public Guid? EntryId { get; private set; }
    public int SortIndex { get; private set; }

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
        bool isDuplicate,
        int sortIndex,
        Guid? suggestedCategoryId = null) => new()
    {
        BatchId = batchId,
        RawJson = rawJson,
        MappedAmount = mappedAmount,
        MappedDate = mappedDate,
        MappedDescription = mappedDescription,
        ImportHash = importHash,
        IsDuplicate = isDuplicate,
        WillImport = !isDuplicate,
        RowStatus = isDuplicate ? ImportRowStatus.Ignored : ImportRowStatus.Pending,
        SuggestedCategoryId = suggestedCategoryId,
        SortIndex = sortIndex,
    };

    public void SetWillImport(bool willImport)
    {
        WillImport = willImport;
        if (RowStatus is ImportRowStatus.Committed)
            return;
        RowStatus = willImport ? ImportRowStatus.Pending : ImportRowStatus.Ignored;
    }

    public void SetSuggestedCategory(Guid? categoryId) => SuggestedCategoryId = categoryId;

    public void Defer()
    {
        EnsureClassifiable();
        RowStatus = ImportRowStatus.Deferred;
        WillImport = true;
    }

    public void Ignore()
    {
        EnsureClassifiable();
        RowStatus = ImportRowStatus.Ignored;
        WillImport = false;
    }

    public void MarkCommitted(Guid entryId)
    {
        EnsureClassifiable();
        if (entryId == Guid.Empty)
            throw new ValidationException("entryId", "Lançamento obrigatório.");
        EntryId = entryId;
        RowStatus = ImportRowStatus.Committed;
        WillImport = false;
        IsDuplicate = false;
    }

    public void MarkSkippedDuplicate()
    {
        EnsureClassifiable();
        RowStatus = ImportRowStatus.Ignored;
        WillImport = false;
        IsDuplicate = true;
    }

    public bool IsOpen => RowStatus is ImportRowStatus.Pending or ImportRowStatus.Deferred;

    private void EnsureClassifiable()
    {
        if (RowStatus == ImportRowStatus.Committed)
            throw new ValidationException("rowId", "Esta linha já foi lançada.");
    }
}
