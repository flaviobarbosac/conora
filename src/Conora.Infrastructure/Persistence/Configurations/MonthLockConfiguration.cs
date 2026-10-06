using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class MonthLockConfiguration : IEntityTypeConfiguration<MonthLock>
{
    public void Configure(EntityTypeBuilder<MonthLock> builder)
    {
        builder.ToTable("month_locks");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.CompetenceYm).IsRequired().HasMaxLength(7);
        builder.Property(e => e.ReopenReason).HasMaxLength(500);
        builder.HasIndex(e => new { e.UsuarioId, e.CompetenceYm })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_month_locks_UsuarioId_Competence");
    }
}
