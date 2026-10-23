namespace Conora.Domain.Enums;

/// <summary>
/// Expense receipt creates a realized Entry; budget boleto creates a planned BudgetLine (payable).
/// </summary>
public enum ReceiptPurpose
{
    Expense = 0,
    BudgetBoleto = 1
}
