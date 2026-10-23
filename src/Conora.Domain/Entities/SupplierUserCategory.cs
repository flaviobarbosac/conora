namespace Conora.Domain.Entities;

/// <summary>Per-user category override for a global supplier. Born when the person edits an entry's category.</summary>
public class SupplierUserCategory : ModelBase
{
    public Guid SupplierId { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string CategoryCode { get; private set; } = default!;

    private SupplierUserCategory()
    {
    }

    public static SupplierUserCategory Create(Guid supplierId, Guid usuarioId, string categoryCode)
    {
        var code = categoryCode.Trim().ToUpperInvariant();
        return new SupplierUserCategory
        {
            SupplierId = supplierId,
            UsuarioId = usuarioId,
            CategoryCode = code
        };
    }

    public void SetCategoryCode(string categoryCode)
    {
        CategoryCode = categoryCode.Trim().ToUpperInvariant();
    }
}
