using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class LifeProjectConfiguration : IEntityTypeConfiguration<LifeProject>
{
    public void Configure(EntityTypeBuilder<LifeProject> builder)
    {
        builder.ToTable("life_projects");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(120);
        builder.Property(e => e.GoalAmount).HasPrecision(18, 2);
        builder.Property(e => e.AccumulatedAmount).HasPrecision(18, 2);
        builder.Property(e => e.DueDate).IsRequired();
        builder.Property(e => e.ContributionStartYm).IsRequired().HasMaxLength(7);
        builder.Property(e => e.Scope).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(e => e.UsuarioId);
        builder.HasIndex(e => new { e.UsuarioId, e.ChartAccountId })
            .IsUnique()
            .HasFilter("\"ChartAccountId\" IS NOT NULL AND \"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_life_projects_UsuarioId_ChartAccountId");
    }
}
