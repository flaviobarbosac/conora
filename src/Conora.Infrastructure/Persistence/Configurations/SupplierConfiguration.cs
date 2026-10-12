using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Cnpj).HasMaxLength(14);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.NormalizedName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.DefaultCategoryCode).HasMaxLength(40);
        builder.HasIndex(e => e.Cnpj).IsUnique().HasFilter("\"Cnpj\" IS NOT NULL");
        builder.HasIndex(e => e.NormalizedName);
        builder.HasQueryFilter(e => e.DeletedAt == null);
    }
}
