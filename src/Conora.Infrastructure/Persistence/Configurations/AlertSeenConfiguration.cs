using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class AlertSeenConfiguration : IEntityTypeConfiguration<AlertSeen>
{
    public void Configure(EntityTypeBuilder<AlertSeen> builder)
    {
        builder.ToTable("alert_seen");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.CompetenceYm).IsRequired().HasMaxLength(7);
        builder.Property(e => e.Code).IsRequired().HasMaxLength(64);
        builder.Property(e => e.SeenAt).IsRequired();
        builder.HasIndex(e => new { e.UsuarioId, e.CompetenceYm, e.Code, e.CategoryId })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_alert_seen_Usuario_Ym_Code_Category");
    }
}
