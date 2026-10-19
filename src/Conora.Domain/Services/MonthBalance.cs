namespace Conora.Domain.Services;

/// <summary>
/// Saldo do mês = renda recebida (inclui receita sem categoria) − descontos − projetos − essencial − social.
/// </summary>
public static class MonthBalance
{
    public static decimal FromReceived(
        decimal receivedIncome,
        decimal discount,
        decimal life,
        decimal essential,
        decimal social)
        => receivedIncome - discount - life - essential - social;
}
