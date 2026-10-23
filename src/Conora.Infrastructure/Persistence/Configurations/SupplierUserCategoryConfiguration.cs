using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class SupplierUserCategoryConfiguration : IEntityTypeConfiguration<SupplierUserCategory>
{
    public void Configure(EntityTypeBuilder<SupplierUserCategory> builder)
    {
        builder.ToTable("supplier_user_categories");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.CategoryCode).IsRequired().HasMaxLength(40);
        builder.HasIndex(e => new { e.SupplierId, e.UsuarioId }).IsUnique();
        builder.HasQueryFilter(e => e.DeletedAt == null);
    }
}
