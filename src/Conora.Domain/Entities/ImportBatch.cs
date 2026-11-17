using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Services;

namespace Conora.Domain.Entities;

public class ImportBatch : ModelBase, ITenantOwned, ICompetenceScoped
{
    public Guid UsuarioId { get; set; }
    public string FileName { get; private set; } = default!;
    public ImportFormat Format { get; private set; }
    public ImportStatus Status { get; private set; }
    public int RowCount { get; private set; }
    public string CompetenceYm { get; private set; } = default!;
    public Guid? AccountId { get; private set; }
    public string? DetectedBankCode { get; private set; }
    public string? DetectedAgency { get; private set; }
    public string? DetectedAccountNumber { get; private set; }
    public string? DetectedCheckDigit { get; private set; }

    private ImportBatch()
    {
    }

    public static ImportBatch Create(
        string fileName,
        ImportFormat format,
        int rowCount,
        string competenceYm,
        string? detectedBankCode = null,
        string? detectedAgency = null,
        string? detectedAccountNumber = null,
        string? detectedCheckDigit = null) => new()
    {
        FileName = string.IsNullOrWhiteSpace(fileName) ? "import" : fileName.Trim(),
        Format = format,
        Status = ImportStatus.Preview,
        RowCount = rowCount,
        CompetenceYm = Competence.Require(competenceYm),
        DetectedBankCode = NormalizeOptional(detectedBankCode, 3),
        DetectedAgency = NormalizeOptional(detectedAgency, 20),
        DetectedAccountNumber = NormalizeOptional(detectedAccountNumber, 20),
        DetectedCheckDigit = NormalizeOptional(detectedCheckDigit, 3),
    };

    public void BindAccount(Guid accountId)
    {
        if (accountId == Guid.Empty)
            throw new ValidationException("accountId", "Conta obrigatória.");
        AccountId = accountId;
        if (Status == ImportStatus.Preview)
            Status = ImportStatus.InProgress;
    }

    public void SetRowCount(int rowCount) => RowCount = rowCount;

    public void Commit() => Status = ImportStatus.Committed;

    public void Discard() => Status = ImportStatus.Discarded;

    public void EnsureOpen()
    {
        if (Status is ImportStatus.Committed or ImportStatus.Discarded)
            throw new ValidationException("batchId", "Esta importação já foi encerrada.");
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
