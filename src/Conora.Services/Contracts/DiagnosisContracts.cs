namespace Conora.Services.Contracts;

public sealed record IncomeSourceRequest(string Name, string CompetenceYm, decimal Gross, decimal Inss, decimal Ir, decimal Tithe);

public sealed record UpdateIncomeSourceRequest(string Name, decimal Gross, decimal Inss, decimal Ir, decimal Tithe);

public sealed record IncomeSourceResponse(
    Guid Id, string Name, string CompetenceYm, decimal Gross, decimal Inss, decimal Ir, decimal Tithe, decimal NetSpendable);

/// <summary>Diagnosis of a competence: income is the net spendable once; tithe is only a planned expense.</summary>
public sealed record DiagnosisSummaryResponse(
    string CompetenceYm,
    decimal Gross,
    decimal Inss,
    decimal Ir,
    decimal NetSpendable,
    decimal PlannedTithe,
    IReadOnlyList<IncomeSourceResponse> Sources);
