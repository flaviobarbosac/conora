using System.Security.Cryptography;
using System.Text;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

/// <summary>CSV/OFX import: preview, mapping and duplicate discard happen before anything is written as an entry.</summary>
public sealed class ImportService
{
    private const int MaxContentChars = 2_000_000;
    private const int MaxRows = 5000;

    private readonly IFinanceRepository _repo;
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ICorrelationContext _correlation;
    private readonly PlanService _plan;
    private readonly MonthService _months;

    public ImportService(
        IFinanceRepository repo,
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        PlanService plan,
        MonthService months)
    {
        _repo = repo;
        _audits = audits;
        _uow = uow;
        _correlation = correlation;
        _plan = plan;
        _months = months;
    }

    public async Task<ImportPreviewResponse> PreviewAsync(ImportPreviewRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ValidationException("content", "Arquivo vazio.");
        if (request.Content.Length > MaxContentChars)
            throw new ValidationException("content", "Arquivo muito grande.");

        var parsed = request.Format == ImportFormat.Ofx
            ? ImportParser.ParseOfx(request.Content)
            : ImportParser.ParseCsv(request.Content, request.Mapping);
        if (parsed.Count > MaxRows)
            throw new ValidationException("content", $"O arquivo excede {MaxRows} linhas.");

        var hashes = BuildHashes(parsed);
        var known = await ExistingHashesAsync(hashes, ct);

        var batch = ImportBatch.Create(request.FileName, request.Format, parsed.Count);
        var rows = parsed
            .Select((p, i) => ImportPreviewRow.Create(
                batch.Id, p.RawJson, p.Amount, p.Date, Truncate(p.Description), hashes[i], known.Contains(hashes[i])))
            .ToList();

        _repo.Add(batch);
        _repo.AddRange(rows);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(batch, rows);
    }

    public async Task<ImportPreviewResponse> GetAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await RequireBatchAsync(batchId, ct, track: false);
        var rows = await _repo.ListAsync<ImportPreviewRow>(r => r.BatchId == batchId, ct);
        return ToResponse(batch, rows);
    }

    public async Task<ImportRowResponse> SetRowAsync(Guid batchId, Guid rowId, SetImportRowRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var batch = await RequireBatchAsync(batchId, ct, track: false);
        EnsurePreview(batch);

        var row = await _repo.FirstOrDefaultAsync<ImportPreviewRow>(r => r.Id == rowId && r.BatchId == batchId, ct)
                  ?? throw new NotFoundException("Linha de importação", rowId);
        row.SetWillImport(request.WillImport);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(row);
    }

    public async Task<CommitImportResponse> CommitAsync(Guid batchId, CommitImportRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var batch = await RequireBatchAsync(batchId, ct);
        EnsurePreview(batch);

        var account = await _repo.GetAsync<Account>(request.AccountId, ct) ?? throw new NotFoundException("Conta", request.AccountId);
        if (account.IsArchived)
            throw new ValidationException("accountId", "A conta está arquivada.");

        var rows = await _repo.ListAsync<ImportPreviewRow>(r => r.BatchId == batchId, ct);
        var selected = rows.Where(r => r.WillImport).ToList();

        await _months.EnsureOpenAsync(selected.Select(r => Competence.From(r.MappedDate)), ct);

        // Re-check duplicates: entries may have been created since the preview.
        var known = await ExistingHashesAsync(selected.Select(r => r.ImportHash).ToList(), ct);
        var entries = new List<Entry>();
        foreach (var row in selected.Where(r => !known.Contains(r.ImportHash)))
        {
            var isIncome = row.MappedAmount > 0;
            entries.Add(Entry.Create(
                isIncome ? EntryType.Income : EntryType.Expense,
                Math.Abs(row.MappedAmount),
                row.MappedDate,
                row.MappedDescription,
                accountId: request.AccountId,
                categoryId: isIncome ? request.DefaultIncomeCategoryId : request.DefaultExpenseCategoryId,
                importHash: row.ImportHash));
        }

        _repo.AddRange(entries);
        account.ApplyDelta(entries.SelectMany(e => e.BalanceEffects()).Sum(x => x.Delta));
        batch.Commit();

        AuditRecorder.Record(_audits, _correlation, "ImportBatch", batch.Id, "ImportCommitted",
            new { batch.FileName, Imported = entries.Count, Skipped = rows.Count - entries.Count });
        await _uow.SaveChangesAsync(ct);
        return new CommitImportResponse(batch.Id, entries.Count, rows.Count - entries.Count);
    }

    private async Task<HashSet<string>> ExistingHashesAsync(IReadOnlyList<string> hashes, CancellationToken ct)
    {
        if (hashes.Count == 0)
            return [];

        var list = hashes.Distinct().ToList();
        var found = await _repo.QueryAsync<Entry, string>(
            q => q.Where(e => e.ImportHash != null && list.Contains(e.ImportHash)).Select(e => e.ImportHash!), ct);
        return found.ToHashSet();
    }

    /// <summary>Hash of date|amount|description; identical rows in the same file get an occurrence suffix.</summary>
    private static List<string> BuildHashes(IReadOnlyList<ParsedImportRow> rows)
    {
        var seen = new Dictionary<string, int>();
        var hashes = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            var key = $"{row.Date:yyyyMMdd}|{row.Amount:0.00}|{row.Description.Trim().ToLowerInvariant()}";
            var occurrence = seen.GetValueOrDefault(key);
            seen[key] = occurrence + 1;
            hashes.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{key}#{occurrence}"))));
        }

        return hashes;
    }

    private async Task<ImportBatch> RequireBatchAsync(Guid id, CancellationToken ct, bool track = true)
        => await _repo.FirstOrDefaultAsync<ImportBatch>(b => b.Id == id, ct, track) ?? throw new NotFoundException("Importação", id);

    private static void EnsurePreview(ImportBatch batch)
    {
        if (batch.Status != ImportStatus.Preview)
            throw new ValidationException("batchId", "A importação já foi confirmada.");
    }

    private static string Truncate(string text) => text.Length <= 250 ? text : text[..250];

    private static ImportRowResponse ToResponse(ImportPreviewRow r)
        => new(r.Id, r.MappedDate, r.MappedAmount, r.MappedDescription, r.IsDuplicate, r.WillImport);

    private static ImportPreviewResponse ToResponse(ImportBatch batch, IReadOnlyList<ImportPreviewRow> rows)
        => new(
            batch.Id,
            batch.FileName,
            batch.Format,
            batch.Status,
            batch.RowCount,
            rows.Count(r => r.IsDuplicate),
            rows.OrderBy(r => r.MappedDate).Select(ToResponse).ToList());
}
