using Conora.Domain.Exceptions;
using Conora.Domain.Services;

namespace Conora.Domain.Entities;

public class IncomeSource : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string Name { get; private set; } = default!;
    public string CompetenceYm { get; private set; } = default!;
    public decimal Gross { get; private set; }
    public decimal Inss { get; private set; }
    public decimal Ir { get; private set; }
    public decimal Tithe { get; private set; }
    public decimal NetSpendable { get; private set; }

    private IncomeSource()
    {
    }

    public static IncomeSource Create(string name, string competenceYm, decimal gross, decimal inss, decimal ir, decimal tithe)
    {
        var source = new IncomeSource { CompetenceYm = Competence.Require(competenceYm) };
        source.Apply(name, gross, inss, ir, tithe);
        return source;
    }

    public void Update(string name, decimal gross, decimal inss, decimal ir, decimal tithe)
        => Apply(name, gross, inss, ir, tithe);

    private void Apply(string name, decimal gross, decimal inss, decimal ir, decimal tithe)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("name", "Nome da fonte é obrigatório.");

        var diagnosis = IncomeDiagnosisCalculator.Calculate(gross, inss, ir, tithe);
        Name = name.Trim();
        Gross = diagnosis.Gross;
        Inss = diagnosis.Inss;
        Ir = diagnosis.Ir;
        Tithe = diagnosis.Tithe;
        NetSpendable = diagnosis.NetSpendable;
    }
}
