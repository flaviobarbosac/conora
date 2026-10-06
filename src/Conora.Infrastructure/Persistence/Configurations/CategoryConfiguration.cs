using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(120);
        builder.Property(e => e.Code).HasMaxLength(60);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => new { e.UsuarioId, e.Code })
            .IsUnique()
            .HasFilter("\"Code\" IS NOT NULL AND \"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_categories_UsuarioId_Code");
        builder.HasIndex(e => new { e.UsuarioId, e.Kind });
    }
}
