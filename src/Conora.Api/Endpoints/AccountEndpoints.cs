using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/accounts").WithTags("Accounts").RequireAuthorization();

        group.MapGet("/", async (AccountService service, CancellationToken ct, bool includeArchived = false) =>
            Results.Ok(await service.ListAsync(includeArchived, ct)));

        group.MapPost("/", async (CreateAccountRequest request, AccountService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(request, ct);
            return Results.Created($"/accounts/{created.Id}", created);
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateAccountRequest request, AccountService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, ct)));

        group.MapPost("/{id:guid}/archive", async (Guid id, ArchiveAccountRequest request, AccountService service, CancellationToken ct) =>
            Results.Ok(await service.ArchiveAsync(id, request.Archived, ct)));

        group.MapDelete("/{id:guid}", async (Guid id, AccountService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPost("/transfers", async (TransferRequest request, AccountService service, CancellationToken ct) =>
            Results.Ok(await service.TransferAsync(request, ct)));

        return app;
    }
}
