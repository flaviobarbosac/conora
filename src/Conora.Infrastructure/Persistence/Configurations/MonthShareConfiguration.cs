using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class MonthShareConfiguration : IEntityTypeConfiguration<MonthShare>
{
    public void Configure(EntityTypeBuilder<MonthShare> builder)
    {
        builder.ToTable("month_shares");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ClientUsuarioId).IsRequired();
        builder.Property(e => e.ConsultantUsuarioId).IsRequired();
        builder.Property(e => e.CompetenceYm).IsRequired().HasMaxLength(7);

        builder.HasIndex(e => new { e.ClientUsuarioId, e.ConsultantUsuarioId, e.CompetenceYm })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_month_shares_client_consultant_month");

        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ClientUsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ConsultantUsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}
