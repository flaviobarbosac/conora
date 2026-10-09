using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class IncomeSourceConfiguration : IEntityTypeConfiguration<IncomeSource>
{
    public void Configure(EntityTypeBuilder<IncomeSource> builder)
    {
        builder.ToTable("income_sources");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(120);
        builder.Property(e => e.CompetenceYm).IsRequired().HasMaxLength(7);
        builder.Property(e => e.Gross).HasPrecision(18, 2);
        builder.Property(e => e.Inss).HasPrecision(18, 2);
        builder.Property(e => e.Ir).HasPrecision(18, 2);
        builder.Property(e => e.Tithe).HasPrecision(18, 2);
        builder.Property(e => e.NetSpendable).HasPrecision(18, 2);
        builder.HasIndex(e => new { e.UsuarioId, e.CompetenceYm });
    }
}
