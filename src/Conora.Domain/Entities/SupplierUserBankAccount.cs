namespace Conora.Domain.Entities;

/// <summary>Per-user preferred bank account for a global supplier (accounts are tenant-owned).</summary>
public class SupplierUserBankAccount : ModelBase
{
    public Guid SupplierId { get; private set; }
    public Guid UsuarioId { get; private set; }
    public Guid AccountId { get; private set; }

    private SupplierUserBankAccount()
    {
    }

    public static SupplierUserBankAccount Create(Guid supplierId, Guid usuarioId, Guid accountId)
    {
        return new SupplierUserBankAccount
        {
            SupplierId = supplierId,
            UsuarioId = usuarioId,
            AccountId = accountId
        };
    }

    public void SetAccountId(Guid accountId) => AccountId = accountId;
}
