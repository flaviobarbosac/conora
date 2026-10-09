using Conora.Services;
using Conora.Services.Contracts;

namespace Conora.Api.Endpoints;

public static class DiagnosisEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosisEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/diagnosis").WithTags("Diagnosis").RequireAuthorization();

        group.MapGet("/{competenceYm}", async (string competenceYm, DiagnosisService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(competenceYm, ct)));

        group.MapPost("/income-sources", async (IncomeSourceRequest request, DiagnosisService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(request, ct);
            return Results.Created($"/diagnosis/income-sources/{created.Id}", created);
        });

        group.MapPut("/income-sources/{id:guid}", async (Guid id, UpdateIncomeSourceRequest request, DiagnosisService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, ct)));

        group.MapDelete("/income-sources/{id:guid}", async (Guid id, DiagnosisService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        return app;
    }
}
