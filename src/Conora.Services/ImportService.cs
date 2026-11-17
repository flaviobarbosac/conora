using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Domain.Services;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

/// <summary>CSV/OFX/PDF import with one-row-at-a-time classification.</summary>
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
    private readonly IConsultantScope _scope;

    public ImportService(
        IFinanceRepository repo,
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        PlanService plan,
        MonthService months,
        IConsultantScope scope)
    {
        _repo = repo;
        _audits = audits;
        _uow = uow;
        _correlation = correlation;
        _plan = plan;
        _months = months;
        _scope = scope;
    }

    public async Task<ImportPreviewResponse> PreviewAsync(ImportPreviewRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ValidationException("content", "Arquivo vazio.");
        if (request.Content.Length > MaxContentChars)
            throw new ValidationException("content", "Arquivo muito grande.");

        var competenceYm = string.IsNullOrWhiteSpace(request.CompetenceYm)
            ? Competence.From(DateTime.UtcNow)
            : Competence.Require(request.CompetenceYm);
        _scope.Ensure(competenceYm);

        await EnsureMonthOpenAsync(competenceYm, ct);

        var hint = ToHint(request.Format);
        var parsed = ImportParser.Parse(hint, request.Content, request.Mapping, fileName: request.FileName);
        return await MaterializeBatchAsync(request.FileName, request.Format, competenceYm, parsed, ct);
    }

    public async Task<ImportPreviewResponse> UploadAsync(
        string fileName,
        string contentType,
        Stream stream,
        string competenceYm,
        CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var ym = Competence.Require(competenceYm);
        _scope.Ensure(ym);
        await EnsureMonthOpenAsync(ym, ct);

        await using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        var bytes = memory.ToArray();
        if (bytes.Length == 0)
            throw new ValidationException("content", "Arquivo vazio.");
        if (bytes.Length > MaxContentChars)
            throw new ValidationException("content", "Arquivo muito grande.");

        var format = DetectFormat(fileName, contentType, bytes);
        ParsedImportFile parsed;
        if (format == ImportFormat.Pdf)
        {
            parsed = ImportParser.ParsePdf(bytes, fileName);
        }
        else
        {
            var content = DecodeText(bytes);
            parsed = ImportParser.Parse(ToHint(format), content, mapping: null, fileName: fileName);
        }

        return await MaterializeBatchAsync(fileName, format, ym, parsed, ct);
    }

    public async Task<ImportPreviewResponse?> GetOpenAsync(string competenceYm, CancellationToken ct)
    {
        var ym = Competence.Require(competenceYm);
        _scope.Ensure(ym);
        var batch = await _repo.FirstOrDefaultAsync<ImportBatch>(
            b => b.CompetenceYm == ym && (b.Status == ImportStatus.Preview || b.Status == ImportStatus.InProgress),
            ct,
            track: false);
        if (batch is null)
            return null;
        var rows = await _repo.ListAsync<ImportPreviewRow>(r => r.BatchId == batch.Id, ct);
        return await ToResponseAsync(batch, rows, ct);
    }

    public async Task<ImportPreviewResponse> GetAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await RequireBatchAsync(batchId, ct, track: false);
        var rows = await _repo.ListAsync<ImportPreviewRow>(r => r.BatchId == batchId, ct);
        return await ToResponseAsync(batch, rows, ct);
    }

    public async Task DiscardAsync(Guid batchId, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var batch = await RequireBatchAsync(batchId, ct);
        batch.EnsureOpen();
        batch.Discard();
        AuditRecorder.Record(_audits, _correlation, "ImportBatch", batch.Id, "ImportDiscarded",
            new { batch.FileName, batch.CompetenceYm });
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ImportPreviewResponse> BindAccountAsync(Guid batchId, BindImportAccountRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var batch = await RequireBatchAsync(batchId, ct);
        batch.EnsureOpen();

        Account account;
        if (request.AccountId is Guid existingId)
        {
            account = await _repo.GetAsync<Account>(existingId, ct) ?? throw new NotFoundException("Conta", existingId);
            if (account.IsArchived)
                throw new ValidationException("accountId", "A conta está arquivada.");
        }
        else if (request.CreateIfMissing)
        {
            if (string.IsNullOrWhiteSpace(batch.DetectedAccountNumber) && string.IsNullOrWhiteSpace(request.Name))
                throw new ValidationException("name", "Informe o nome da conta para cadastrar.");

            if (!await _plan.HasFullAccessAsync(ct))
            {
                var active = await _repo.CountAsync<Account>(a => !a.IsArchived, ct);
                if (active >= FreeTierLimits.MaxBankAccounts)
                    throw new ValidationException(
                        "name",
                        "No Conora grátis você cadastra só uma conta, usada nos lançamentos. Assine para incluir mais contas.");
            }

            var name = string.IsNullOrWhiteSpace(request.Name)
                ? BuildDetectedAccountName(batch)
                : request.Name.Trim();
            account = Account.Create(
                name,
                request.Kind ?? AccountKind.Checking,
                batch.DetectedBankCode,
                batch.DetectedAgency,
                batch.DetectedAccountNumber,
                batch.DetectedCheckDigit);
            _repo.Add(account);
        }
        else
            throw new ValidationException("accountId", "Selecione uma conta ou confirme o cadastro.");

        batch.BindAccount(account.Id);
        await _uow.SaveChangesAsync(ct);
        var rows = await _repo.ListAsync<ImportPreviewRow>(r => r.BatchId == batchId, ct);
        return await ToResponseAsync(batch, rows, ct);
    }

    public async Task<ImportRowResponse> SetRowAsync(Guid batchId, Guid rowId, SetImportRowRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var batch = await RequireBatchAsync(batchId, ct, track: false);
        batch.EnsureOpen();

        var row = await _repo.FirstOrDefaultAsync<ImportPreviewRow>(r => r.Id == rowId && r.BatchId == batchId, ct)
                  ?? throw new NotFoundException("Linha de importação", rowId);
        row.SetWillImport(request.WillImport);
        await _uow.SaveChangesAsync(ct);
        return await ToRowResponseAsync(row, ct);
    }

    public async Task<CommitImportResponse> CommitAsync(Guid batchId, CommitImportRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var batch = await RequireBatchAsync(batchId, ct);
        batch.EnsureOpen();

        var account = await _repo.GetAsync<Account>(request.AccountId, ct) ?? throw new NotFoundException("Conta", request.AccountId);
        if (account.IsArchived)
            throw new ValidationException("accountId", "A conta está arquivada.");

        batch.BindAccount(request.AccountId);
        var rows = await _repo.ListAsync<ImportPreviewRow>(r => r.BatchId == batchId, ct);
        var selected = rows.Where(r => r.WillImport && r.RowStatus is ImportRowStatus.Pending or ImportRowStatus.Deferred).ToList();

        await _months.EnsureOpenAsync(selected.Select(r => Competence.From(r.MappedDate)), ct);

        var known = await ExistingHashesAsync(selected.Select(r => r.ImportHash).ToList(), ct);
        var entries = new List<Entry>();
        foreach (var row in selected.Where(r => !known.Contains(r.ImportHash)))
        {
            var isIncome = row.MappedAmount > 0;
            var entry = Entry.Create(
                isIncome ? EntryType.Income : EntryType.Expense,
                Math.Abs(row.MappedAmount),
                row.MappedDate,
                row.MappedDescription,
                accountId: request.AccountId,
                categoryId: isIncome ? request.DefaultIncomeCategoryId : request.DefaultExpenseCategoryId,
                importHash: row.ImportHash);
            entries.Add(entry);
            row.MarkCommitted(entry.Id);
        }

        foreach (var row in selected.Where(r => known.Contains(r.ImportHash)))
            row.MarkSkippedDuplicate();

        _repo.AddRange(entries);
        account.ApplyDelta(entries.SelectMany(e => e.BalanceEffects()).Sum(x => x.Delta));
        if (rows.All(r => !r.IsOpen))
            batch.Commit();

        AuditRecorder.Record(_audits, _correlation, "ImportBatch", batch.Id, "ImportCommitted",
            new { batch.FileName, Imported = entries.Count, Skipped = selected.Count - entries.Count });
        await _uow.SaveChangesAsync(ct);
        return new CommitImportResponse(batch.Id, entries.Count, selected.Count - entries.Count);
    }

    public async Task<ImportCurrentRowResponse?> GetCurrentAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await RequireBatchAsync(batchId, ct, track: false);
        batch.EnsureOpen();
        if (batch.AccountId is null)
            throw new ValidationException("accountId", "Vincule a conta bancária antes de classificar.");

        var rows = await _repo.ListAsync<ImportPreviewRow>(r => r.BatchId == batchId, ct);
        var open = rows
            .Where(r => r.RowStatus == ImportRowStatus.Pending)
            .OrderBy(r => r.SortIndex)
            .ThenBy(r => r.MappedDate)
            .ToList();
        if (open.Count == 0)
        {
            open = rows
                .Where(r => r.RowStatus == ImportRowStatus.Deferred)
                .OrderBy(r => r.SortIndex)
                .ThenBy(r => r.MappedDate)
                .ToList();
        }

        if (open.Count == 0)
            return null;

        var current = open[0];
        if (current.SuggestedCategoryId is null)
        {
            var suggestion = await SuggestCategoryAsync(current.MappedDescription, current.MappedAmount, ct);
            if (suggestion is not null)
            {
                var tracked = await _repo.FirstOrDefaultAsync<ImportPreviewRow>(r => r.Id == current.Id, ct)
                              ?? current;
                tracked.SetSuggestedCategory(suggestion.Value);
                await _uow.SaveChangesAsync(ct);
                current = tracked;
            }
        }

        var totalOpen = rows.Count(r => r.IsOpen);
        var position = rows.Count(r => r.RowStatus is ImportRowStatus.Committed or ImportRowStatus.Ignored) + 1;
        return new ImportCurrentRowResponse(
            batch.Id,
            await ToRowResponseAsync(current, ct),
            position,
            totalOpen,
            rows.Count(r => r.RowStatus == ImportRowStatus.Deferred),
            rows.Count(r => r.RowStatus == ImportRowStatus.Committed),
            rows.Count,
            current.IsDuplicate || await HasExactDuplicateAsync(current, ct));
    }

    public async Task<ImportCurrentRowResponse?> CommitRowAsync(Guid batchId, Guid rowId, ClassifyImportRowRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var batch = await RequireBatchAsync(batchId, ct);
        batch.EnsureOpen();
        if (batch.AccountId is null)
            throw new ValidationException("accountId", "Vincule a conta bancária antes de classificar.");

        var account = await _repo.GetAsync<Account>(batch.AccountId.Value, ct)
                      ?? throw new NotFoundException("Conta", batch.AccountId.Value);
        var row = await _repo.FirstOrDefaultAsync<ImportPreviewRow>(r => r.Id == rowId && r.BatchId == batchId, ct)
                  ?? throw new NotFoundException("Linha de importação", rowId);
        if (!row.IsOpen)
            throw new ValidationException("rowId", "Esta linha já foi tratada.");

        await _months.EnsureOpenAsync([Competence.From(row.MappedDate)], ct);

        var category = await _repo.GetAsync<Category>(request.CategoryId, ct)
                       ?? throw new NotFoundException("Categoria", request.CategoryId);

        if (await HasExactDuplicateAsync(row, ct) && !request.ConfirmDuplicate)
            throw new ValidationException("confirmDuplicate", "Já existe um lançamento com esta data, valor e descrição. Confirme se deseja lançar de novo.");

        var isIncome = row.MappedAmount > 0;
        var entry = Entry.Create(
            isIncome ? EntryType.Income : EntryType.Expense,
            Math.Abs(row.MappedAmount),
            row.MappedDate,
            row.MappedDescription,
            accountId: account.Id,
            categoryId: category.Id,
            importHash: row.ImportHash);
        _repo.Add(entry);
        account.ApplyDelta(entry.BalanceEffects().Sum(x => x.Delta));
        row.SetSuggestedCategory(category.Id);
        row.MarkCommitted(entry.Id);
        await _uow.SaveChangesAsync(ct);

        var remaining = await _repo.ListAsync<ImportPreviewRow>(r => r.BatchId == batchId, ct, track: false);
        if (remaining.All(r => !r.IsOpen))
        {
            batch.Commit();
            await _uow.SaveChangesAsync(ct);
        }

        return await GetCurrentAsync(batchId, ct);
    }

    public async Task<ImportCurrentRowResponse?> DeferRowAsync(Guid batchId, Guid rowId, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var batch = await RequireBatchAsync(batchId, ct, track: false);
        batch.EnsureOpen();
        var row = await _repo.FirstOrDefaultAsync<ImportPreviewRow>(r => r.Id == rowId && r.BatchId == batchId, ct)
                  ?? throw new NotFoundException("Linha de importação", rowId);
        row.Defer();
        await _uow.SaveChangesAsync(ct);
        return await GetCurrentAsync(batchId, ct);
    }

    public async Task<ImportCurrentRowResponse?> IgnoreRowAsync(Guid batchId, Guid rowId, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var batch = await RequireBatchAsync(batchId, ct);
        batch.EnsureOpen();
        var row = await _repo.FirstOrDefaultAsync<ImportPreviewRow>(r => r.Id == rowId && r.BatchId == batchId, ct)
                  ?? throw new NotFoundException("Linha de importação", rowId);
        row.Ignore();
        var rows = await _repo.ListAsync<ImportPreviewRow>(r => r.BatchId == batchId, ct);
        if (rows.All(r => !r.IsOpen))
            batch.Commit();
        await _uow.SaveChangesAsync(ct);
        return await GetCurrentAsync(batchId, ct);
    }

    public async Task SkipDuplicateAsync(Guid batchId, Guid rowId, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var batch = await RequireBatchAsync(batchId, ct);
        batch.EnsureOpen();
        var row = await _repo.FirstOrDefaultAsync<ImportPreviewRow>(r => r.Id == rowId && r.BatchId == batchId, ct)
                  ?? throw new NotFoundException("Linha de importação", rowId);
        row.MarkSkippedDuplicate();
        var rows = await _repo.ListAsync<ImportPreviewRow>(r => r.BatchId == batchId, ct);
        if (rows.All(r => !r.IsOpen))
            batch.Commit();
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ImportReportResponse> GetReportAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await RequireBatchAsync(batchId, ct, track: false);
        var rows = await _repo.ListAsync<ImportPreviewRow>(r => r.BatchId == batchId, ct);
        var mapped = new List<ImportRowResponse>();
        foreach (var row in rows.OrderBy(r => r.SortIndex).ThenBy(r => r.MappedDate))
            mapped.Add(await ToRowResponseAsync(row, ct));

        return new ImportReportResponse(
            batch.Id,
            batch.CompetenceYm,
            batch.FileName,
            mapped.Count(r => r.RowStatus == ImportRowStatus.Pending),
            mapped.Count(r => r.RowStatus == ImportRowStatus.Deferred),
            mapped.Count(r => r.RowStatus == ImportRowStatus.Committed),
            mapped.Count(r => r.RowStatus == ImportRowStatus.Ignored),
            mapped.Where(r => r.RowStatus == ImportRowStatus.Pending).ToList(),
            mapped.Where(r => r.RowStatus == ImportRowStatus.Deferred).ToList(),
            mapped.Where(r => r.RowStatus == ImportRowStatus.Committed).ToList(),
            mapped.Where(r => r.RowStatus == ImportRowStatus.Ignored).ToList());
    }

    private async Task<ImportPreviewResponse> MaterializeBatchAsync(
        string fileName,
        ImportFormat format,
        string competenceYm,
        ParsedImportFile parsed,
        CancellationToken ct)
    {
        if (parsed.Rows.Count > MaxRows)
            throw new ValidationException("content", $"O arquivo excede {MaxRows} linhas.");

        EnsureSingleMonth(parsed.Rows, competenceYm);

        var open = await _repo.ListAsync<ImportBatch>(
            b => b.CompetenceYm == competenceYm && (b.Status == ImportStatus.Preview || b.Status == ImportStatus.InProgress),
            ct,
            track: true);

        HashSet<string> committedHashes = [];
        Guid? carryAccountId = null;
        foreach (var previous in open)
        {
            var previousRows = await _repo.ListAsync<ImportPreviewRow>(r => r.BatchId == previous.Id, ct, track: true);
            foreach (var row in previousRows.Where(r => r.RowStatus == ImportRowStatus.Committed))
                committedHashes.Add(row.ImportHash);
            carryAccountId ??= previous.AccountId;
            previous.Discard();
        }

        var hashes = BuildHashes(parsed.Rows);
        var known = await ExistingHashesAsync(hashes, ct);
        known.UnionWith(committedHashes);

        var batch = ImportBatch.Create(
            fileName,
            format,
            parsed.Rows.Count,
            competenceYm,
            parsed.Account?.BankCode,
            parsed.Account?.Agency,
            parsed.Account?.AccountNumber,
            parsed.Account?.CheckDigit);

        var matched = await FindMatchingAccountAsync(parsed.Account, ct);
        if (matched is not null)
            batch.BindAccount(matched.Id);
        else if (carryAccountId is Guid previousAccountId)
            batch.BindAccount(previousAccountId);

        var rows = new List<ImportPreviewRow>();
        for (var i = 0; i < parsed.Rows.Count; i++)
        {
            var p = parsed.Rows[i];
            var isDup = known.Contains(hashes[i]);
            rows.Add(ImportPreviewRow.Create(
                batch.Id,
                p.RawJson,
                p.Amount,
                p.Date,
                Truncate(p.Description),
                hashes[i],
                isDup,
                i));
        }

        _repo.Add(batch);
        _repo.AddRange(rows);
        await _uow.SaveChangesAsync(ct);
        return await ToResponseAsync(batch, rows, ct);
    }

    private async Task EnsureMonthOpenAsync(string competenceYm, CancellationToken ct)
    {
        var lockRow = await _repo.FirstOrDefaultAsync<MonthLock>(m => m.CompetenceYm == competenceYm, ct, track: false);
        if (lockRow?.IsClosed == true)
            throw new ValidationException(
                "competenceYm",
                $"O mês {competenceYm} está fechado. Reabra o mês em Fechar mês para importar o extrato.");
    }

    private static void EnsureSingleMonth(IReadOnlyList<ParsedImportRow> rows, string competenceYm)
    {
        var outside = rows
            .Select(r => new { Row = r, Ym = Competence.From(r.Date) })
            .Where(x => x.Ym != competenceYm)
            .OrderBy(x => x.Row.Date)
            .ToList();
        if (outside.Count == 0)
            return;

        var monthName = MonthLabel(competenceYm);
        var fileMonths = outside
            .Select(x => x.Ym)
            .Distinct()
            .OrderBy(x => x)
            .Select(MonthLabel)
            .ToList();
        var fileLabel = fileMonths.Count == 1
            ? fileMonths[0]
            : string.Join(", ", fileMonths);
        throw new ValidationException(
            "content",
            $"Este extrato é de {fileLabel}. Nesta importação só entram dias de {monthName}. Abra a competência {fileLabel} e envie de novo.");
    }

    private static string MonthLabel(string competenceYm)
    {
        var parts = competenceYm.Split('-');
        if (parts.Length == 2
            && int.TryParse(parts[0], out var year)
            && int.TryParse(parts[1], out var month))
        {
            return new DateTime(year, month, 1).ToString("MMMM", new CultureInfo("pt-BR"));
        }

        return competenceYm;
    }

    private async Task<Account?> FindMatchingAccountAsync(ParsedImportAccount? detected, CancellationToken ct)
    {
        if (detected is null)
            return null;

        var accounts = await _repo.ListAsync<Account>(a => !a.IsArchived, ct, track: false);
        return accounts.FirstOrDefault(a => AccountsMatch(a, detected));
    }

    private static bool AccountsMatch(Account account, ParsedImportAccount detected)
    {
        if (!string.IsNullOrWhiteSpace(detected.BankCode)
            && !string.Equals(account.BankCode, detected.BankCode.PadLeft(3, '0'), StringComparison.OrdinalIgnoreCase)
            && !string.Equals(account.BankCode, detected.BankCode, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(detected.Agency)
            && !string.Equals(NormalizeDigits(account.Agency), NormalizeDigits(detected.Agency), StringComparison.Ordinal))
            return false;

        if (string.IsNullOrWhiteSpace(detected.AccountNumber))
            return false;

        var left = NormalizeDigits(account.AccountNumber);
        var right = NormalizeDigits(detected.AccountNumber);
        if (!string.Equals(left, right, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrWhiteSpace(detected.CheckDigit)
            && !string.IsNullOrWhiteSpace(account.CheckDigit)
            && !string.Equals(account.CheckDigit, detected.CheckDigit, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static string NormalizeDigits(string? value)
        => string.IsNullOrWhiteSpace(value) ? "" : new string(value.Where(char.IsDigit).ToArray());

    private static string BuildDetectedAccountName(ImportBatch batch)
    {
        var bank = BrazilianBanks.Find(batch.DetectedBankCode)?.Name ?? batch.DetectedBankCode ?? "Banco";
        var number = batch.DetectedAccountNumber ?? "conta";
        return $"{bank} {number}";
    }

    private async Task<Guid?> SuggestCategoryAsync(string description, decimal amount, CancellationToken ct)
    {
        var normalized = NormalizeDescription(description);
        if (normalized.Length < 3)
            return null;

        var isIncome = amount > 0;
        var recent = await _repo.QueryAsync<Entry, Entry>(q => q
            .Where(e => e.CategoryId != null
                        && (isIncome ? e.Type == EntryType.Income : e.Type == EntryType.Expense))
            .OrderByDescending(e => e.OccurredAt)
            .Take(300), ct);

        var exact = recent.FirstOrDefault(e => NormalizeDescription(e.Description) == normalized);
        if (exact?.CategoryId is Guid exactId)
            return exactId;

        var token = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(w => w.Length >= 3);
        if (token is null)
            return null;

        var ranked = recent
            .Where(e => NormalizeDescription(e.Description).Contains(token, StringComparison.Ordinal))
            .GroupBy(e => e.CategoryId!.Value)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
        return ranked == Guid.Empty ? null : ranked;
    }

    private static string NormalizeDescription(string description)
        => string.Join(' ', description.ToLowerInvariant()
            .Split([' ', '-', '/', ',', '.', ';'], StringSplitOptions.RemoveEmptyEntries));

    private async Task<bool> HasExactDuplicateAsync(ImportPreviewRow row, CancellationToken ct)
    {
        if (await _repo.AnyAsync<Entry>(e => e.ImportHash == row.ImportHash, ct))
            return true;

        return await _repo.AnyAsync<Entry>(e =>
            e.OccurredAt == row.MappedDate
            && e.Amount == Math.Abs(row.MappedAmount)
            && e.Description.ToLower() == row.MappedDescription.Trim().ToLower(), ct);
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

    private static string Truncate(string text) => text.Length <= 250 ? text : text[..250];

    private static ImportFormatHint ToHint(ImportFormat format) => format switch
    {
        ImportFormat.Ofx => ImportFormatHint.Ofx,
        ImportFormat.Pdf => ImportFormatHint.Pdf,
        _ => ImportFormatHint.Csv,
    };

    private static ImportFormat DetectFormat(string fileName, string contentType, byte[] bytes)
    {
        var name = fileName.ToLowerInvariant();
        if (name.EndsWith(".pdf") || contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
            return ImportFormat.Pdf;
        if (name.EndsWith(".ofx") || name.EndsWith(".qfx") || contentType.Contains("ofx", StringComparison.OrdinalIgnoreCase))
            return ImportFormat.Ofx;
        if (name.EndsWith(".csv") || contentType.Contains("csv", StringComparison.OrdinalIgnoreCase))
            return ImportFormat.Csv;

        if (bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
            return ImportFormat.Pdf;

        var head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 200));
        if (head.Contains("OFX", StringComparison.OrdinalIgnoreCase) || head.Contains("<STMTTRN>", StringComparison.OrdinalIgnoreCase))
            return ImportFormat.Ofx;

        return ImportFormat.Csv;
    }

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Encoding.UTF8.GetString(bytes);
    }

    private async Task<ImportRowResponse> ToRowResponseAsync(ImportPreviewRow r, CancellationToken ct)
    {
        string? categoryName = null;
        if (r.SuggestedCategoryId is Guid categoryId)
        {
            var category = await _repo.FirstOrDefaultAsync<Category>(c => c.Id == categoryId, ct, track: false);
            categoryName = category?.Name;
        }

        return new ImportRowResponse(
            r.Id,
            r.MappedDate,
            r.MappedAmount,
            r.MappedDescription,
            r.IsDuplicate,
            r.WillImport,
            r.RowStatus,
            r.SuggestedCategoryId,
            categoryName,
            r.EntryId,
            r.SortIndex);
    }

    private async Task<ImportPreviewResponse> ToResponseAsync(ImportBatch batch, IReadOnlyList<ImportPreviewRow> rows, CancellationToken ct)
    {
        var mapped = new List<ImportRowResponse>();
        foreach (var row in rows.OrderBy(r => r.SortIndex).ThenBy(r => r.MappedDate))
            mapped.Add(await ToRowResponseAsync(row, ct));

        var detected = batch.DetectedBankCode is null
                       && batch.DetectedAgency is null
                       && batch.DetectedAccountNumber is null
            ? null
            : new ImportDetectedAccountResponse(
                batch.DetectedBankCode,
                batch.DetectedAgency,
                batch.DetectedAccountNumber,
                batch.DetectedCheckDigit,
                BrazilianBanks.Find(batch.DetectedBankCode)?.Name);

        var accountMatched = batch.AccountId is not null;
        var accountNeedsCreate = !accountMatched && detected?.AccountNumber is not null;

        return new ImportPreviewResponse(
            batch.Id,
            batch.FileName,
            batch.Format,
            batch.Status,
            batch.CompetenceYm,
            batch.RowCount,
            mapped.Count(r => r.IsDuplicate),
            mapped.Count(r => r.RowStatus == ImportRowStatus.Pending),
            mapped.Count(r => r.RowStatus == ImportRowStatus.Deferred),
            mapped.Count(r => r.RowStatus == ImportRowStatus.Committed),
            mapped.Count(r => r.RowStatus == ImportRowStatus.Ignored),
            batch.AccountId,
            accountMatched,
            accountNeedsCreate,
            detected,
            mapped);
    }
}
