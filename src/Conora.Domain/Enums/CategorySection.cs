namespace Conora.Domain.Enums;

/// <summary>
/// Category sections: structural masters (Budget, Expense, Patrimony) plus the posting
/// sections that sit under them (Income…Liability).
/// </summary>
public enum CategorySection
{
    Income = 1,
    Discount = 2,
    LifeProject = 3,
    Essential = 4,
    Social = 5,
    Asset = 6,
    Liability = 7,
    /// <summary>Master: monthly cash flow (Orçamento).</summary>
    Budget = 8,
    /// <summary>Structural group under Budget (Despesa).</summary>
    Expense = 9,
    /// <summary>Master: balance sheet (Patrimônio).</summary>
    Patrimony = 10
}
