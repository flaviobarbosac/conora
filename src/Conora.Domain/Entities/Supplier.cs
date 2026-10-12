using Conora.Domain.Exceptions;
using Conora.Domain;

namespace Conora.Domain.Entities;

/// <summary>Global supplier registry (no tenant). Classification is a system Category Code only.</summary>
public class Supplier : ModelBase
{
    public string? Cnpj { get; private set; }
    public string Name { get; private set; } = default!;
    public string NormalizedName { get; private set; } = default!;
    public string? DefaultCategoryCode { get; private set; }
    public bool NeedsReview { get; private set; }
    public Guid? ClassifiedByUsuarioId { get; private set; }
    public DateTime? ClassifiedAt { get; private set; }

    private Supplier()
    {
    }

    public static Supplier Create(string name, string? cnpj)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ValidationException("name", "Nome do fornecedor é obrigatório.");

        string? normalizedCnpj = null;
        var needsReview = true;
        if (!string.IsNullOrWhiteSpace(cnpj))
        {
            if (!BrazilianCnpj.TryNormalize(cnpj, out normalizedCnpj))
                throw new ValidationException("cnpj", "CNPJ inválido.");
            needsReview = false;
        }

        return new Supplier
        {
            Cnpj = normalizedCnpj,
            Name = trimmed.Length > 200 ? trimmed[..200] : trimmed,
            NormalizedName = NormalizeName(trimmed),
            NeedsReview = needsReview
        };
    }

    public void UpdateName(string name)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;
        Name = trimmed.Length > 200 ? trimmed[..200] : trimmed;
        NormalizedName = NormalizeName(trimmed);
    }

    /// <summary>Sets classification once. Does not overwrite an existing code.</summary>
    public bool TrySetClassification(string categoryCode, Guid classifiedByUsuarioId)
    {
        if (!string.IsNullOrWhiteSpace(DefaultCategoryCode))
            return false;

        var code = categoryCode.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
            throw new ValidationException("categoryCode", "Código da conta é obrigatório.");

        DefaultCategoryCode = code;
        ClassifiedByUsuarioId = classifiedByUsuarioId;
        ClassifiedAt = DateTime.UtcNow;
        NeedsReview = Cnpj is null;
        return true;
    }

    public static string NormalizeName(string name)
    {
        var form = name.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = form.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
            != System.Globalization.UnicodeCategory.NonSpacingMark);
        return string.Concat(chars).Normalize(System.Text.NormalizationForm.FormC);
    }
}
