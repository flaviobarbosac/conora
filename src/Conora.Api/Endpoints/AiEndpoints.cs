using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ai").WithTags("AI").RequireAuthorization();

        group.MapPost("/ask", async (AskAiRequest request, GeminiService service, CancellationToken ct) =>
            Results.Ok(await service.AskAsync(request, ct)));

        return app;
    }
}
