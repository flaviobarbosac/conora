using Conora.Domain.Exceptions;
using Conora.Domain.Services;

namespace Conora.Domain.Entities;

public class LifeProject : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string Name { get; private set; } = default!;
    public decimal GoalAmount { get; private set; }
    public DateTime? DueDate { get; private set; }
    public decimal AccumulatedAmount { get; private set; }

    private LifeProject()
    {
    }

    public static LifeProject Create(string name, decimal goalAmount, DateTime? dueDate)
    {
        var project = new LifeProject();
        project.Update(name, goalAmount, dueDate);
        return project;
    }

    public void Update(string name, decimal goalAmount, DateTime? dueDate)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["Nome do projeto é obrigatório."];
        if (goalAmount <= 0) errors["goalAmount"] = ["Meta deve ser maior que zero."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        Name = name.Trim();
        GoalAmount = decimal.Round(goalAmount, 2);
        DueDate = dueDate is null ? null : Competence.ToUtc(dueDate.Value);
    }

    /// <summary>Adds (or removes, when negative) a contribution. No yield is modeled (spec C10).</summary>
    public void ApplyContribution(decimal delta) => AccumulatedAmount = Math.Max(0, AccumulatedAmount + delta);
}
