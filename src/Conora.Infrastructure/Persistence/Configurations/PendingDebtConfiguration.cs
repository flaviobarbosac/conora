using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class PendingDebtConfiguration : IEntityTypeConfiguration<PendingDebt>
{
    public void Configure(EntityTypeBuilder<PendingDebt> builder)
    {
        builder.ToTable("pending_debts");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.CompetenceYm).HasMaxLength(7).IsRequired();
        builder.Property(e => e.OriginalAmount).HasPrecision(18, 2);
        builder.Property(e => e.OpenAmount).HasPrecision(18, 2);
        builder.HasIndex(e => new { e.UsuarioId, e.CompetenceYm, e.CategoryId })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_pending_debts_User_Month_Category");
    }
}
