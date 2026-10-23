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

    [Fact]
    public void User_override_stores_code_per_person()
    {
        var supplier = Supplier.Create("Padaria X", "04.252.011/0001-10");
        supplier.TrySetClassification("ESS_MARKET", Guid.CreateVersion7());
        var user = Guid.CreateVersion7();
        var overrideRow = SupplierUserCategory.Create(supplier.Id, user, "ESS_FOOD");
        Assert.Equal("ESS_FOOD", overrideRow.CategoryCode);
        overrideRow.SetCategoryCode("ess_transport");
        Assert.Equal("ESS_TRANSPORT", overrideRow.CategoryCode);
    }
}
