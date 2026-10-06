using Conora.Domain.Exceptions;

namespace Conora.Domain.Services;

public readonly record struct IncomeDiagnosis(decimal Gross, decimal Inss, decimal Ir, decimal Tithe, decimal NetSpendable);

/// <summary>
/// Income diagnosis rule (spec v1.1 §3): NetSpendable = Gross - INSS - IR.
/// Tithe is NOT deducted from income; it becomes a planned expense in Contribuições/Doações.
/// </summary>
public static class IncomeDiagnosisCalculator
{
    public const decimal SuggestedTitheRate = 0.10m;

    public static IncomeDiagnosis Calculate(decimal gross, decimal inss, decimal ir, decimal tithe)
    {
        var errors = new Dictionary<string, string[]>();
        if (gross < 0) errors["gross"] = ["Renda bruta não pode ser negativa."];
        if (inss < 0) errors["inss"] = ["INSS não pode ser negativo."];
        if (ir < 0) errors["ir"] = ["IR não pode ser negativo."];
        if (tithe < 0) errors["tithe"] = ["Dízimo não pode ser negativo."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        var net = decimal.Round(gross - inss - ir, 2);
        if (net < 0)
            throw new ValidationException("gross", "INSS e IR não podem superar a renda bruta.");

        return new IncomeDiagnosis(gross, inss, ir, tithe, net);
    }

    public static decimal SuggestTithe(decimal netSpendable)
        => decimal.Round(netSpendable * SuggestedTitheRate, 2);
}
