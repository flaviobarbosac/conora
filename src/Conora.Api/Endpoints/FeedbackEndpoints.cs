using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class FeedbackEndpoints
{
    public static IEndpointRouteBuilder MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/feedback").WithTags("Feedback").RequireAuthorization();

        group.MapGet("/", async (FeedbackService service, CancellationToken ct) =>
            Results.Ok(await service.ListMineAsync(ct)));

        group.MapGet("/{id:guid}", async (Guid id, FeedbackService service, CancellationToken ct) =>
        {
            var dto = await service.GetMineAsync(id, ct);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        group.MapPost("/", async (CreateFeedbackRequest request, FeedbackService service, CancellationToken ct) =>
        {
            var dto = await service.CreateAsync(request, ct);
            return Results.Ok(dto);
        });

        group.MapPost("/{id:guid}/leitura", async (Guid id, FeedbackService service, CancellationToken ct) =>
        {
            var dto = await service.MarkReplyReadAsync(id, ct);
            return dto is null ? Results.NotFound() : Results.NoContent();
        });

        return app;
    }
}
