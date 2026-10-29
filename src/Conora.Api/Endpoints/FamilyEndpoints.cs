using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class FamilyEndpoints
{
    public static IEndpointRouteBuilder MapFamilyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/family/invites/{token}/preview", async (string token, FamilyGroupService service, CancellationToken ct) =>
            Results.Ok(await service.PreviewInviteAsync(token, ct)))
            .WithTags("Family");

        var group = app.MapGroup("/family").WithTags("Family").RequireAuthorization();

        group.MapGet("/profile", async (FamilyGroupService service, CancellationToken ct) =>
            Results.Ok(await service.GetProfileAsync(ct)));

        group.MapPut("/profile", async (UpdateProfileRequest request, FamilyGroupService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateProfileAsync(request, ct)));

        group.MapPut("/preferences", async (UpdatePreferencesRequest request, FamilyGroupService service, CancellationToken ct) =>
            Results.Ok(await service.UpdatePreferencesAsync(request, ct)));

        group.MapPost("/onboarding/complete", async (FamilyGroupService service, CancellationToken ct) =>
            Results.Ok(await service.CompleteOnboardingAsync(ct)));

        group.MapPost("/profile/contract-acknowledgment", async (FamilyGroupService service, CancellationToken ct) =>
            Results.Ok(await service.AcknowledgeContractAsync(ct)));

        group.MapPost("/profile/photo", async (HttpRequest request, FamilyGroupService service, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Envie multipart/form-data com o campo file." });

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "Arquivo de imagem obrigatório." });

            await using var stream = file.OpenReadStream();
            return Results.Ok(await service.UploadPhotoAsync(stream, file.ContentType ?? "image/jpeg", ct));
        });

        group.MapGet("/profile/photo", async (FamilyGroupService service, CancellationToken ct) =>
        {
            var photo = await service.GetPhotoAsync(ct);
            if (photo is null)
                return Results.NotFound();
            return Results.File(photo.Content, photo.ContentType, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{photo.ContentHash}\""));
        });

        group.MapDelete("/profile/photo", async (FamilyGroupService service, CancellationToken ct) =>
            Results.Ok(await service.DeletePhotoAsync(ct)));

        group.MapGet("/group", async (FamilyGroupService service, CancellationToken ct) =>
            Results.Ok(await service.GetGroupAsync(ct)));

        group.MapPost("/invites", async (FamilyInviteRequest request, FamilyGroupService service, CancellationToken ct) =>
            Results.Ok(await service.InviteAsync(request, ct)));

        group.MapPost("/invites/qr", async (FamilyGroupService service, CancellationToken ct) =>
            Results.Ok(await service.CreateQrInviteAsync(ct)));

        group.MapDelete("/invites/{id:guid}", async (Guid id, FamilyGroupService service, CancellationToken ct) =>
        {
            await service.CancelInviteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPost("/invites/{token}/accept", async (string token, FamilyGroupService service, CancellationToken ct) =>
            Results.Ok(await service.AcceptInviteAsync(token, ct)));

        group.MapPost("/leave", async (FamilyGroupService service, CancellationToken ct) =>
        {
            await service.LeaveAsync(ct);
            return Results.NoContent();
        });

        group.MapDelete("/members/{usuarioId:guid}", async (Guid usuarioId, FamilyGroupService service, CancellationToken ct) =>
        {
            await service.RemoveMemberAsync(usuarioId, ct);
            return Results.NoContent();
        });

        group.MapPost("/owner-transfer", async (FamilyOwnerTransferRequest request, FamilyGroupService service, CancellationToken ct) =>
            Results.Ok(await service.RequestOwnerTransferAsync(request, ct)));

        group.MapDelete("/owner-transfer", async (FamilyGroupService service, CancellationToken ct) =>
        {
            await service.CancelOwnerTransferAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/owner-transfer/{token}/accept", async (string token, FamilyGroupService service, CancellationToken ct) =>
            Results.Ok(await service.AcceptOwnerTransferAsync(token, ct)));

        group.MapPost("/owner-transfer/store-cancelled", async (FamilyGroupService service, CancellationToken ct) =>
        {
            await service.ConfirmStoreCancelledAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/notices/{id:guid}/read", async (Guid id, FamilyGroupService service, CancellationToken ct) =>
        {
            await service.MarkNoticeReadAsync(id, ct);
            return Results.NoContent();
        });

        return app;
    }
}
