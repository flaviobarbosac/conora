using Conora.Domain.Entities;
using Xunit;

namespace Conora.UnitTests;

public class SupplierClassificationTests
{
    [Fact]
    public void TrySetClassification_sets_once_and_does_not_overwrite()
    {
        var supplier = Supplier.Create("Padaria X", "04.252.011/0001-10");
        var userA = Guid.CreateVersion7();
        var userB = Guid.CreateVersion7();

        Assert.True(supplier.TrySetClassification("ESS_MARKET", userA));
        Assert.Equal("ESS_MARKET", supplier.DefaultCategoryCode);
        Assert.Equal(userA, supplier.ClassifiedByUsuarioId);

        Assert.False(supplier.TrySetClassification("ESS_FOOD", userB));
        Assert.Equal("ESS_MARKET", supplier.DefaultCategoryCode);
        Assert.Equal(userA, supplier.ClassifiedByUsuarioId);
    }

    [Fact]
    public void Create_without_cnpj_marks_needs_review()
    {
        var supplier = Supplier.Create("Feira da esquina", null);
        Assert.Null(supplier.Cnpj);
        Assert.True(supplier.NeedsReview);
    }
}
