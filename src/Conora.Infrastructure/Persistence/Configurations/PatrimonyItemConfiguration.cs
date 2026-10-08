using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class PatrimonyItemConfiguration : IEntityTypeConfiguration<PatrimonyItem>
{
    public void Configure(EntityTypeBuilder<PatrimonyItem> builder)
    {
        builder.ToTable("patrimony_items");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(120);
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.HasIndex(e => new { e.UsuarioId, e.ChartAccountId })
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_patrimony_items_UsuarioId_ChartAccount");
    }
}
