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

        group.MapGet("/group", async (FamilyGroupService service, CancellationToken ct) =>
            Results.Ok(await service.GetGroupAsync(ct)));

        group.MapPost("/invites", async (FamilyInviteRequest request, FamilyGroupService service, CancellationToken ct) =>
            Results.Ok(await service.InviteAsync(request, ct)));

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

        group.MapPost("/notices/{id:guid}/read", async (Guid id, FamilyGroupService service, CancellationToken ct) =>
        {
            await service.MarkNoticeReadAsync(id, ct);
            return Results.NoContent();
        });

        return app;
    }
}
