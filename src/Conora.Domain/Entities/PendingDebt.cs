using Conora.Domain.Exceptions;
using Conora.Domain.Services;

namespace Conora.Domain.Entities;

/// <summary>
/// Unpaid budget gap the user marked as a debt when closing a month.
/// One row per user, reference month and category.
/// </summary>
public class PendingDebt : ModelBase, ITenantOwned, ICompetenceScoped
{
    public Guid UsuarioId { get; set; }
    public string CompetenceYm { get; private set; } = default!;
    public Guid CategoryId { get; private set; }
    public decimal OriginalAmount { get; private set; }
    public decimal OpenAmount { get; private set; }
    public DateTime? PaidAt { get; private set; }

    private PendingDebt()
    {
    }

    public static PendingDebt Open(string competenceYm, Guid categoryId, decimal amount)
    {
        if (categoryId == Guid.Empty)
            throw new ValidationException("categoryId", "Informe a categoria da dívida.");
        if (amount <= 0)
            throw new ValidationException("amount", "O valor em aberto deve ser maior que zero.");

        var rounded = decimal.Round(amount, 2);
        return new PendingDebt
        {
            CompetenceYm = Competence.Require(competenceYm),
            CategoryId = categoryId,
            OriginalAmount = rounded,
            OpenAmount = rounded
        };
    }

    public bool IsOpen => PaidAt is null && OpenAmount > 0;

    /// <summary>
    /// Applies up to <paramref name="amount"/> against the open balance.
    /// Returns how much was used. PaidAt is set only when the balance reaches zero.
    /// </summary>
    public decimal ApplyPayment(decimal amount)
    {
        if (!IsOpen)
            throw new ValidationException("pendingDebtId", "Esta dívida já foi quitada.");
        if (amount <= 0)
            throw new ValidationException("amount", "Valor deve ser maior que zero.");

        var applied = decimal.Round(Math.Min(amount, OpenAmount), 2);
        OpenAmount = decimal.Round(OpenAmount - applied, 2);
        if (OpenAmount <= 0)
        {
            OpenAmount = 0;
            PaidAt = DateTime.UtcNow;
        }

        return applied;
    }
}
