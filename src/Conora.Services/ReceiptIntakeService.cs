using System.Globalization;
using System.Text.Json;
using Conora.Domain;
using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Conora.Services;

public sealed class ReceiptIntakeService
{
    private static readonly TimeSpan DefaultImageTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(10);

    private readonly IFinanceRepository _repo;
    private readonly ISupplierStore _suppliers;
    private readonly IReceiptImageStore _images;
    private readonly IUnitOfWork _uow;
    private readonly ITenantContext _tenant;
    private readonly ICorrelationContext _correlation;
    private readonly IAuditEventRepository _audits;
    private readonly IGeminiClient _gemini;
    private readonly PlanService _plan;
    private readonly CategoryService _categories;
    private readonly EntryService _entries;
    private readonly AccountService _accounts;
    private readonly IReceiptOcrQueue _ocrQueue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ReceiptIntakeService> _logger;
    private readonly TimeSpan _imageTtl;

    public ReceiptIntakeService(
        IFinanceRepository repo,
        ISupplierStore suppliers,
        IReceiptImageStore images,
        IUnitOfWork uow,
        ITenantContext tenant,
        ICorrelationContext correlation,
        IAuditEventRepository audits,
        IGeminiClient gemini,
        PlanService plan,
        CategoryService categories,
        EntryService entries,
        AccountService accounts,
        IReceiptOcrQueue ocrQueue,
        IServiceScopeFactory scopes,
        IConfiguration configuration,
        ILogger<ReceiptIntakeService> logger)
    {
        _repo = repo;
        _suppliers = suppliers;
        _images = images;
        _uow = uow;
        _tenant = tenant;
        _correlation = correlation;
        _audits = audits;
        _gemini = gemini;
        _plan = plan;
        _categories = categories;
        _entries = entries;
        _accounts = accounts;
        _ocrQueue = ocrQueue;
        _scopes = scopes;
        _logger = logger;
        _imageTtl = TimeSpan.TryParse(configuration["Receipts:ImageTtl"], out var ttl) && ttl > TimeSpan.Zero
            ? ttl
            : DefaultImageTtl;
    }

    public async Task<ReceiptIntakeResponse> CreateAsync(
        Stream image,
        string contentType,
        ReceiptChannel channel,
        CancellationToken ct,
        string? phoneE164 = null)
    {
        await _plan.EnsureWritableAsync(ct);

        var key = await _images.SaveAsync(image, contentType, ct);
        var intake = ReceiptIntake.Create(channel, key, _imageTtl, phoneE164);
        _repo.Add(intake);
        AuditRecorder.Record(_audits, _correlation, "ReceiptIntake", intake.Id, "ReceiptIntakeCreated",
            new { channel, key });
        await _uow.SaveChangesAsync(ct);

        EnqueueProcess(intake.Id, _tenant.UsuarioId
            ?? throw new UnauthorizedAccessException("Tenant não autenticado."));

        return await ToResponseAsync(intake, ct);
    }

    public async Task<ReceiptIntakeResponse> CreateFromBytesAsync(
        byte[] bytes,
        string contentType,
        ReceiptChannel channel,
        CancellationToken ct,
        string? phoneE164 = null)
    {
        await using var stream = new MemoryStream(bytes);
        return await CreateAsync(stream, contentType, channel, ct, phoneE164);
    }

    public async Task<ReceiptIntakeResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var intake = await _repo.GetAsync<ReceiptIntake>(id, ct)
                     ?? throw new NotFoundException("Recibo", id);
        return await ToResponseAsync(intake, ct);
    }

    public async Task<ReceiptIntakeResponse> ClassifyAsync(Guid id, ClassifyReceiptRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var intake = await _repo.GetAsync<ReceiptIntake>(id, ct)
                     ?? throw new NotFoundException("Recibo", id);

        var code = request.CategoryCode.Trim().ToUpperInvariant();
        await _categories.GetByCodeAsync(code, ct);

        Guid? supplierId = null;
        string? cnpj = null;
        // Global registry only when CNPJ is present; otherwise classification is local to this intake.
        if (!string.IsNullOrWhiteSpace(intake.ExtractedCnpj)
            && BrazilianCnpj.TryNormalize(intake.ExtractedCnpj, out var normalized))
        {
            var supplier = await EnsureSupplierAsync(intake, ct);
            var userId = _tenant.UsuarioId
                         ?? throw new UnauthorizedAccessException("Tenant não autenticado.");
            supplier.TrySetClassification(code, userId);
            supplierId = supplier.Id;
            cnpj = supplier.Cnpj;
        }

        intake.SetClassification(code, supplierId);
        AuditRecorder.Record(_audits, _correlation, "ReceiptIntake", intake.Id, "ReceiptClassified",
            new { code, supplierId, cnpj });
        await _uow.SaveChangesAsync(ct);
        return await ToResponseAsync(intake, ct);
    }

    public async Task<EntryResponse> ConfirmAsync(Guid id, ConfirmReceiptRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var intake = await _repo.GetAsync<ReceiptIntake>(id, ct)
                     ?? throw new NotFoundException("Recibo", id);

        if (intake.Status == ReceiptIntakeStatus.NeedsClassification)
            throw new ValidationException("status", "Classifique o fornecedor antes de confirmar.");
        if (intake.Status == ReceiptIntakeStatus.Processing)
            throw new ValidationException("status", "Aguarde o processamento do recibo.");
        if (intake.Status == ReceiptIntakeStatus.Failed)
            throw new ValidationException("status", intake.FailureReason ?? "Falha ao ler o recibo.");
        if (intake.Status == ReceiptIntakeStatus.Confirmed)
            throw new ValidationException("status", "Este recibo já foi lançado.");

        var amount = request.Amount ?? intake.ExtractedAmount
                     ?? throw new ValidationException("amount", "Informe o valor do lançamento.");
        var occurredAt = request.OccurredAt ?? intake.ExtractedOccurredAt ?? DateTime.UtcNow;
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? intake.ExtractedName ?? "Recibo"
            : request.Description.Trim();

        Guid? categoryId = request.CategoryId;
        if (categoryId is null && !string.IsNullOrWhiteSpace(intake.ProposedCategoryCode))
        {
            var category = await _categories.GetByCodeAsync(intake.ProposedCategoryCode, ct);
            categoryId = category.Id;
        }

        var accountId = request.AccountId ?? intake.SelectedBankAccountId;
        if (accountId is null)
        {
            var accounts = await _accounts.ListAsync(includeArchived: false, ct);
            accountId = accounts.FirstOrDefault()?.Id;
        }

        if (await IsDuplicateAsync(description, amount, occurredAt, ct) && !request.ConfirmDuplicate)
            throw new ValidationException("duplicate",
                "Já existe um lançamento parecido nos últimos minutos. Confirme a duplicata se for intencional.");

        IReadOnlyList<EntryResponse> created = [];
        await _uow.ExecuteInTransactionAsync(async token =>
        {
            created = await _entries.CreateAsync(new CreateEntryRequest(
                EntryType.Expense,
                amount,
                occurredAt,
                description,
                AccountId: accountId,
                CategoryId: categoryId,
                ConfirmDuplicate: request.ConfirmDuplicate), token);

            intake.Confirm();
            if (!string.IsNullOrWhiteSpace(intake.ImageObjectKey))
            {
                await _images.DeleteAsync(intake.ImageObjectKey, token);
                intake.ClearImageKey();
            }

            AuditRecorder.Record(_audits, _correlation, "ReceiptIntake", intake.Id, "ReceiptConfirmed",
                new { EntryId = created[0].Id, description });
            await _uow.SaveChangesAsync(token);
        }, ct);

        return created[0];
    }

    /// <summary>Processes OCR for intakes still in Processing (worker / background).</summary>
    public async Task ProcessAsync(Guid intakeId, CancellationToken ct)
    {
        var intake = await _repo.GetAsync<ReceiptIntake>(intakeId, ct);
        if (intake is null || intake.Status != ReceiptIntakeStatus.Processing)
            return;

        try
        {
            var bytes = await _images.ReadAsync(intake.ImageObjectKey, ct);
            if (bytes is null || bytes.Length == 0)
            {
                intake.MarkFailed("Imagem do recibo não encontrada.");
                await _uow.SaveChangesAsync(ct);
                return;
            }

            var mime = intake.ImageObjectKey.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? "image/png"
                : "image/jpeg";

            var extracted = await ExtractAsync(bytes, mime, ct);
            string? cnpj = extracted.Cnpj;
            if (cnpj is null && BrazilianCnpj.TryExtract(extracted.RawText, out var fromText))
                cnpj = fromText;

            string? name = string.IsNullOrWhiteSpace(extracted.Name) ? null : extracted.Name.Trim();
            decimal? amount = extracted.Amount is > 0 and <= 99_999_999.99m
                ? decimal.Round(extracted.Amount.Value, 2)
                : null;
            DateTime? occurredAt = extracted.OccurredAt;

            Guid? supplierId = null;
            string? proposedCode = null;

            if (!string.IsNullOrWhiteSpace(cnpj) && BrazilianCnpj.TryNormalize(cnpj, out var normalized))
            {
                cnpj = normalized;
                var supplier = await _suppliers.FindByCnpjAsync(normalized, ct);
                if (supplier is null)
                {
                    supplier = Supplier.Create(name ?? $"CNPJ {normalized}", normalized);
                    _suppliers.Add(supplier);
                    await _uow.SaveChangesAsync(ct);
                }
                else if (!string.IsNullOrWhiteSpace(name))
                {
                    supplier.UpdateName(name);
                }

                supplierId = supplier.Id;
                name ??= supplier.Name;
                proposedCode = supplier.DefaultCategoryCode;
            }
            else
            {
                // Without CNPJ: do not promote a global classification; ask user to classify locally only.
                cnpj = null;
                proposedCode = null;
            }

            intake.ApplyOcr(cnpj, name, amount, occurredAt, supplierId, proposedCode);
            await _uow.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "OCR failed for intake {Id}", intakeId);
            intake.MarkFailed("Não foi possível ler o recibo.");
            await _uow.SaveChangesAsync(ct);
        }
    }

    public async Task<int> ProcessPendingBatchAsync(int take, CancellationToken ct)
    {
        var batch = await _ocrQueue.ListProcessingAsync(take, ct);
        var count = 0;
        foreach (var (id, usuarioId) in batch)
        {
            using var scope = _scopes.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenant.Set(usuarioId);
            var service = scope.ServiceProvider.GetRequiredService<ReceiptIntakeService>();
            await service.ProcessAsync(id, ct);
            count++;
        }

        return count;
    }

    public async Task<int> PurgeExpiredImagesAsync(CancellationToken ct)
    {
        var expired = await _ocrQueue.ListExpiredImagesAsync(200, ct);
        var count = 0;
        foreach (var (id, usuarioId, key) in expired)
        {
            using var scope = _scopes.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenant.Set(usuarioId);
            var repo = scope.ServiceProvider.GetRequiredService<IFinanceRepository>();
            var images = scope.ServiceProvider.GetRequiredService<IReceiptImageStore>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var intake = await repo.GetAsync<ReceiptIntake>(id, ct);
            if (intake is null)
                continue;
            if (!string.IsNullOrWhiteSpace(key))
                await images.DeleteAsync(key, ct);
            intake.ClearImageKey();
            await uow.SaveChangesAsync(ct);
            count++;
        }

        return count;
    }

    public async Task<IReadOnlyList<CategoryResponse>> ListClassifiableCategoriesAsync(CancellationToken ct)
    {
        var all = await _categories.ListAsync(null, false, true, ct);
        return all
            .Where(c => c.AcceptsPosting
                        && c.Code is not null
                        && SystemCategories.IsCashFlowSection(c.Section)
                        && c.Section != CategorySection.Income)
            .ToList();
    }

    public async Task ReopenClassificationAsync(Guid id, CancellationToken ct)
    {
        var intake = await _repo.GetAsync<ReceiptIntake>(id, ct)
                     ?? throw new NotFoundException("Recibo", id);
        intake.ReopenClassification();
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ReceiptIntake?> FindActiveWhatsAppAsync(string phoneE164, CancellationToken ct)
    {
        var phone = WhatsAppLink.NormalizePhone(phoneE164);
        var items = await _repo.ListAsync<ReceiptIntake>(
            i => i.PhoneE164 == phone
                 && i.WhatsAppAwaiting != WhatsAppReceiptAwaiting.None
                 && (i.Status == ReceiptIntakeStatus.NeedsClassification || i.Status == ReceiptIntakeStatus.Ready),
            ct,
            track: true);
        return items.OrderByDescending(i => i.CreatedAt).FirstOrDefault();
    }

    private async Task<Supplier> EnsureSupplierAsync(ReceiptIntake intake, CancellationToken ct)
    {
        if (intake.SupplierId is Guid sid)
        {
            var existing = await _suppliers.GetAsync(sid, ct);
            if (existing is not null)
                return existing;
        }

        if (string.IsNullOrWhiteSpace(intake.ExtractedCnpj)
            || !BrazilianCnpj.TryNormalize(intake.ExtractedCnpj, out var cnpj))
            throw new ValidationException("cnpj", "CNPJ inválido para cadastro global.");

        var supplier = await _suppliers.FindByCnpjAsync(cnpj, ct);
        if (supplier is null)
        {
            supplier = Supplier.Create(intake.ExtractedName ?? $"CNPJ {cnpj}", cnpj);
            _suppliers.Add(supplier);
            await _uow.SaveChangesAsync(ct);
        }

        return supplier;
    }

    private async Task<bool> IsDuplicateAsync(string description, decimal amount, DateTime occurredAt, CancellationToken ct)
    {
        var from = DateTime.UtcNow.Subtract(DedupWindow);
        var day = occurredAt.Date;
        return await _repo.AnyAsync<Entry>(
            e => e.CreatedAt >= from
                 && e.Amount == amount
                 && e.Description == description
                 && e.OccurredAt.Date == day,
            ct);
    }

    private async Task<OcrResult> ExtractAsync(byte[] bytes, string mime, CancellationToken ct)
    {
        if (!_gemini.IsConfigured)
            return new OcrResult(null, null, null, null, null);

        const string system =
            "Você lê cupons fiscais brasileiros. Responda estritamente em JSON: " +
            "{\"cnpj\":\"somente dígitos ou null\",\"name\":\"razão social ou nome fantasia\",\"amount\":número ou null,\"date\":\"yyyy-MM-dd ou null\",\"rawText\":\"trecho útil\"}. " +
            "amount é o valor total pago. Se não houver CNPJ, cnpj=null.";
        var raw = await _gemini.GenerateJsonFromImageAsync(system, "Extraia os dados do cupom.", bytes, mime, ct);
        if (raw is null)
            return new OcrResult(null, null, null, null, null);

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            string? cnpj = root.TryGetProperty("cnpj", out var c) && c.ValueKind != JsonValueKind.Null
                ? c.GetString()
                : null;
            string? name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            decimal? amount = null;
            if (root.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetDecimal(out var amt))
                amount = amt;
            DateTime? occurredAt = null;
            if (root.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String
                && DateTime.TryParse(d.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                occurredAt = DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
            var rawText = root.TryGetProperty("rawText", out var r) ? r.GetString() : null;
            if (!string.IsNullOrWhiteSpace(cnpj) && BrazilianCnpj.TryNormalize(cnpj, out var normalized))
                cnpj = normalized;
            else if (!string.IsNullOrWhiteSpace(cnpj) && BrazilianCnpj.TryExtract(cnpj, out var extracted))
                cnpj = extracted;
            else
                cnpj = null;

            return new OcrResult(cnpj, name, amount, occurredAt, rawText);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return new OcrResult(null, null, null, null, raw);
        }
    }

    private void EnqueueProcess(Guid intakeId, Guid usuarioId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                tenant.Set(usuarioId);
                var service = scope.ServiceProvider.GetRequiredService<ReceiptIntakeService>();
                await service.ProcessAsync(intakeId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background OCR failed for {Id}", intakeId);
            }
        });
    }

    private async Task<ReceiptIntakeResponse> ToResponseAsync(ReceiptIntake intake, CancellationToken ct)
    {
        Guid? categoryId = null;
        string? categoryName = null;
        if (!string.IsNullOrWhiteSpace(intake.ProposedCategoryCode))
        {
            try
            {
                var category = await _categories.GetByCodeAsync(intake.ProposedCategoryCode, ct);
                categoryId = category.Id;
                categoryName = category.Name;
            }
            catch (NotFoundException)
            {
                // code may be stale
            }
        }

        return new ReceiptIntakeResponse(
            intake.Id,
            intake.Channel,
            intake.Status,
            intake.ExtractedCnpj,
            intake.ExtractedName,
            intake.ExtractedAmount,
            intake.ExtractedOccurredAt,
            intake.SupplierId,
            intake.ProposedCategoryCode,
            categoryId,
            categoryName,
            intake.SelectedBankAccountId,
            intake.FailureReason,
            intake.Status == ReceiptIntakeStatus.NeedsClassification);
    }

    private sealed record OcrResult(
        string? Cnpj,
        string? Name,
        decimal? Amount,
        DateTime? OccurredAt,
        string? RawText);
}
