using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Services;

namespace Conora.Domain.Entities;

public class LifeProject : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string Name { get; private set; } = default!;
    public decimal GoalAmount { get; private set; }
    public DateTime DueDate { get; private set; }
    public string ContributionStartYm { get; private set; } = default!;
    public decimal AccumulatedAmount { get; private set; }
    public LifeProjectScope Scope { get; private set; } = LifeProjectScope.Personal;
    public Guid? ChartAccountId { get; private set; }

    private LifeProject()
    {
    }

    public static LifeProject Create(
        string name,
        decimal goalAmount,
        DateTime dueDate,
        string contributionStartYm,
        Guid? chartAccountId,
        LifeProjectScope scope = LifeProjectScope.Personal)
    {
        var project = new LifeProject { Scope = scope };
        project.Update(name, goalAmount, dueDate, contributionStartYm, chartAccountId, scope);
        return project;
    }

    public void Update(
        string name,
        decimal goalAmount,
        DateTime dueDate,
        string contributionStartYm,
        Guid? chartAccountId,
        LifeProjectScope? scope = null)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["Nome do projeto é obrigatório."];
        if (goalAmount <= 0) errors["goalAmount"] = ["Meta deve ser maior que zero."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        var startYm = Competence.Require(contributionStartYm, "contributionStartYm");
        var dueYm = Competence.From(dueDate);
        Competence.RangeInclusive(startYm, dueYm);

        Name = name.Trim();
        GoalAmount = decimal.Round(goalAmount, 2);
        DueDate = Competence.ToUtc(dueDate);
        ContributionStartYm = startYm;
        ChartAccountId = chartAccountId;
        if (scope is not null)
            Scope = scope.Value;
    }

    /// <summary>Adds (or removes, when negative) a contribution. No yield is modeled (spec C10).</summary>
    public void ApplyContribution(decimal delta) => AccumulatedAmount = Math.Max(0, AccumulatedAmount + delta);
}
