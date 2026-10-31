using System.Security.Cryptography;
using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed partial class FamilyGroupService
{
    private static readonly HashSet<string> AllowedPhotoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp",
    };

    private const long MaxPhotoBytes = 2 * 1024 * 1024;

    public async Task<ProfileResponse> GetProfileAsync(CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        return ToProfile(user);
    }

    public async Task<ProfileResponse> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await RequireUserAsync(ct, track: true);
        user.Rename(request.Name);
        try
        {
            user.UpdateContact(request.Phone, request.Cep);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(ex.ParamName == "cep" ? "cep" : "phone", ex.Message);
        }
        await _uow.SaveChangesAsync(ct);
        return ToProfile(user);
    }

    public async Task<ProfileResponse> UpdatePreferencesAsync(UpdatePreferencesRequest request, CancellationToken ct)
    {
        var user = await RequireUserAsync(ct, track: true);
        user.UpdatePreferences(
            ThemeCatalog.Normalize(request.ThemeId),
            request.QuietMode,
            request.EmailsEnabled,
            request.ReserveTargetMonths,
            request.MonthCloseDay,
            request.IncludeAbsoluteValuesInRetrospective,
            request.VoiceEntryConfirm,
            request.BudgetAlertPercent,
            request.PhotoEvolutionEnabled,
            request.HomeJourneyTimelineEnabled);
        await _uow.SaveChangesAsync(ct);

        if (request.EmailsEnabled)
            await _emailSuppressions.RemoveUserOptOutAsync(user.Email, ct);
        else
            await _emailSuppressions.UpsertAsync(user.Email, EmailSuppression.ReasonUserOptOut, null, ct);

        return ToProfile(user);
    }

    public async Task<ProfileResponse> CompleteOnboardingAsync(CancellationToken ct)
    {
        var user = await RequireUserAsync(ct, track: true);
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(user.Phone))
            errors["phone"] = ["Informe o telefone."];
        if (string.IsNullOrWhiteSpace(user.Cep))
            errors["cep"] = ["Informe o CEP."];
        if (!await _repo.AnyAsync<Account>(a => !a.IsArchived, ct))
            errors["account"] = ["Cadastre ao menos uma conta bancária."];
        // Credit card is optional on onboard (available later under Contas).
        if (errors.Count > 0)
            throw new ValidationException(errors);

        user.CompleteOnboarding();
        await _uow.SaveChangesAsync(ct);
        await _categories.EnsureDefaultsAsync(ct);
        return ToProfile(user);
    }

    public async Task<ProfileResponse> AcknowledgeContractAsync(CancellationToken ct)
    {
        var user = await RequireUserAsync(ct, track: true);
        user.AcknowledgeContract();
        await _uow.SaveChangesAsync(ct);
        return ToProfile(user);
    }

    public async Task<ProfileResponse> UploadPhotoAsync(Stream content, string contentType, CancellationToken ct)
    {
        if (content is null || !content.CanRead)
            throw new ValidationException("file", "Arquivo de imagem obrigatório.");

        var normalizedType = NormalizePhotoContentType(contentType);
        if (normalizedType is null)
            throw new ValidationException("file", "Use JPEG, PNG ou WebP.");

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length == 0)
            throw new ValidationException("file", "Arquivo de imagem obrigatório.");
        if (buffer.Length > MaxPhotoBytes)
            throw new ValidationException("file", "A foto deve ter no máximo 2 MB.");

        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        buffer.Position = 0;
        var key = await _photos.SaveAsync(buffer, normalizedType, ct);

        var user = await RequireUserAsync(ct, track: true);
        var previousKey = user.PhotoObjectKey;
        try
        {
            user.SetPhoto(key, hash, normalizedType);
        }
        catch (ArgumentException ex)
        {
            await _photos.DeleteAsync(key, ct);
            throw new ValidationException("file", ex.Message);
        }

        await _uow.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(previousKey) &&
            !string.Equals(previousKey, key, StringComparison.Ordinal))
            await _photos.DeleteAsync(previousKey, ct);

        return ToProfile(user);
    }

    public async Task<ProfilePhotoContent?> GetPhotoAsync(CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        if (string.IsNullOrEmpty(user.PhotoObjectKey) ||
            string.IsNullOrEmpty(user.PhotoContentType) ||
            string.IsNullOrEmpty(user.PhotoContentHash))
            return null;

        var bytes = await _photos.ReadAsync(user.PhotoObjectKey, ct);
        if (bytes is null || bytes.Length == 0)
            return null;

        return new ProfilePhotoContent(bytes, user.PhotoContentType, user.PhotoContentHash);
    }

    public async Task<ProfileResponse> DeletePhotoAsync(CancellationToken ct)
    {
        var user = await RequireUserAsync(ct, track: true);
        var previousKey = user.PhotoObjectKey;
        user.ClearPhoto();
        await _uow.SaveChangesAsync(ct);
        if (!string.IsNullOrEmpty(previousKey))
            await _photos.DeleteAsync(previousKey, ct);
        return ToProfile(user);
    }

    private static string? NormalizePhotoContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return null;
        var type = contentType.Split(';', 2)[0].Trim();
        if (!AllowedPhotoTypes.Contains(type))
            return null;
        return type.Equals("image/jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : type.ToLowerInvariant();
    }

    private async Task<User> RequireUserAsync(CancellationToken ct, bool track = false)
    {
        var id = RequireUserId();
        return await _family.FindUserByIdAsync(id, ct, track)
               ?? throw new UserNotFoundException(id);
    }
}
