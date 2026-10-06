using Conora.Services;

namespace Conora.Api.Endpoints;

public static class HelpEndpoints
{
    public static IEndpointRouteBuilder MapHelpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/help").WithTags("Help").RequireAuthorization();

        group.MapGet("/", (HelpService service) => Results.Ok(service.Get()));

        group.MapGet("/steps/{key}", (string key, HelpService service) =>
            service.GetStep(key) is { } step ? Results.Ok(step) : Results.NotFound());

        return app;
    }
}
