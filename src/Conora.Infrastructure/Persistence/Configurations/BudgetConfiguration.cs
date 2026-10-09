using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.ToTable("budgets");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.CompetenceYm).IsRequired().HasMaxLength(7);
        builder.Property(e => e.Mode).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(e => new { e.UsuarioId, e.CompetenceYm })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_budgets_UsuarioId_Competence");
    }
}
