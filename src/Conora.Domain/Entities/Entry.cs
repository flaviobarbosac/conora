using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Services;

namespace Conora.Domain.Entities;

/// <summary>Single source of truth for money movement (spec v1.1 §2).</summary>
public class Entry : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public EntryType Type { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public string CompetenceYm { get; private set; } = default!;
    public Guid? AccountId { get; private set; }
    public Guid? ContraAccountId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public Guid? IncomeSourceId { get; private set; }
    public Guid? CreditCardId { get; private set; }
    public Guid? LifeProjectId { get; private set; }
    public Guid? MemberId { get; private set; }
    public string Description { get; private set; } = default!;
    public string? RecurrenceKey { get; private set; }
    public int? InstallmentNumber { get; private set; }
    public int? InstallmentCount { get; private set; }
    public string? ImportHash { get; private set; }

    /// <summary>Only income, expense and contribution entries move the monthly result.</summary>
    public bool AffectsMonthlyResult => Type is EntryType.Income or EntryType.Expense or EntryType.Contribution;

    private Entry()
    {
    }

    public static Entry Create(
        EntryType type,
        decimal amount,
        DateTime occurredAt,
        string description,
        string? competenceYm = null,
        Guid? accountId = null,
        Guid? contraAccountId = null,
        Guid? categoryId = null,
        Guid? incomeSourceId = null,
        Guid? creditCardId = null,
        Guid? lifeProjectId = null,
        Guid? memberId = null,
        string? recurrenceKey = null,
        int? installmentNumber = null,
        int? installmentCount = null,
        string? importHash = null)
    {
        var entry = new Entry
        {
            Type = type,
            CreditCardId = creditCardId,
            LifeProjectId = lifeProjectId,
            RecurrenceKey = recurrenceKey,
            InstallmentNumber = installmentNumber,
            InstallmentCount = installmentCount,
            ImportHash = importHash
        };
        entry.Apply(amount, occurredAt, competenceYm, accountId, contraAccountId, categoryId, incomeSourceId, memberId, description);
        return entry;
    }

    public void Update(
        decimal amount,
        DateTime occurredAt,
        string? competenceYm,
        Guid? accountId,
        Guid? contraAccountId,
        Guid? categoryId,
        Guid? incomeSourceId,
        Guid? memberId,
        string description)
        => Apply(amount, occurredAt, competenceYm, accountId, contraAccountId, categoryId, incomeSourceId, memberId, description);

    /// <summary>Signed balance effects of this entry on accounts. Transfers net to zero across accounts.</summary>
    public IEnumerable<(Guid AccountId, decimal Delta)> BalanceEffects()
    {
        switch (Type)
        {
            case EntryType.Income:
                if (AccountId is Guid income) yield return (income, Amount);
                break;
            case EntryType.Transfer:
                if (AccountId is Guid from) yield return (from, -Amount);
                if (ContraAccountId is Guid to) yield return (to, Amount);
                break;
            default:
                if (AccountId is Guid debit) yield return (debit, -Amount);
                break;
        }
    }

    private void Apply(
        decimal amount,
        DateTime occurredAt,
        string? competenceYm,
        Guid? accountId,
        Guid? contraAccountId,
        Guid? categoryId,
        Guid? incomeSourceId,
        Guid? memberId,
        string description)
    {
        var errors = new Dictionary<string, string[]>();
        if (amount <= 0) errors["amount"] = ["Valor deve ser maior que zero."];
        if (string.IsNullOrWhiteSpace(description)) errors["description"] = ["Descrição é obrigatória."];

        switch (Type)
        {
            case EntryType.Transfer:
                if (accountId is null || contraAccountId is null)
                    errors["accountId"] = ["Transferência exige conta de origem e de destino."];
                else if (accountId == contraAccountId)
                    errors["contraAccountId"] = ["Origem e destino devem ser contas diferentes."];
                break;
            case EntryType.CardPayment:
                if (CreditCardId is null) errors["creditCardId"] = ["Pagamento de fatura exige o cartão."];
                if (accountId is null) errors["accountId"] = ["Pagamento de fatura exige a conta de débito."];
                break;
            case EntryType.ProjectContribution:
                if (LifeProjectId is null) errors["lifeProjectId"] = ["Aporte exige o projeto de vida."];
                break;
        }

        if (errors.Count > 0)
            throw new ValidationException(errors);

        var date = Competence.ToUtc(occurredAt);
        Amount = decimal.Round(amount, 2);
        OccurredAt = date;
        CompetenceYm = string.IsNullOrWhiteSpace(competenceYm) ? Competence.From(date) : Competence.Require(competenceYm);
        AccountId = accountId;
        ContraAccountId = Type == EntryType.Transfer ? contraAccountId : null;
        CategoryId = Type == EntryType.Transfer ? null : categoryId;
        IncomeSourceId = Type == EntryType.Income ? incomeSourceId : null;
        MemberId = memberId;
        Description = description.Trim();
    }
}
