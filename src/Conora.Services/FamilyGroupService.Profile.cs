using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Services;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Conora.Services;

public sealed partial class FamilyGroupService
{
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
            request.PhotoEvolutionEnabled);
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
        return ToProfile(user);
    }

    public async Task<ProfileResponse> AcknowledgeContractAsync(CancellationToken ct)
    {
        var user = await RequireUserAsync(ct, track: true);
        user.AcknowledgeContract();
        await _uow.SaveChangesAsync(ct);
        return ToProfile(user);
    }

    private async Task<User> RequireUserAsync(CancellationToken ct, bool track = false)
    {
        var id = RequireUserId();
        return await _family.FindUserByIdAsync(id, ct, track)
               ?? throw new UserNotFoundException(id);
    }
}
