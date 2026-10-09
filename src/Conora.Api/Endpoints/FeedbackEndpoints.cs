using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class FeedbackEndpoints
{
    public static IEndpointRouteBuilder MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/feedback").WithTags("Feedback").RequireAuthorization();

        group.MapPost("/", async (FeedbackRequest request, FeedbackService service, CancellationToken ct) =>
        {
            await service.SubmitAsync(request, ct);
            return Results.NoContent();
        });

        return app;
    }
}
