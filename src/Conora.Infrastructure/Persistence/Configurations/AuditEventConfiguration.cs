using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.EntityName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.EntityId).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Action).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Actor).IsRequired().HasMaxLength(200);
        builder.Property(e => e.TimestampUtc).IsRequired();
        builder.Property(e => e.CorrelationId).IsRequired().HasMaxLength(100);
        builder.Property(e => e.DetailsJson).IsRequired().HasColumnType("jsonb");
        builder.Property(e => e.Version).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();
        builder.Property(e => e.DeletedAt);

        builder.HasIndex(e => e.UsuarioId).HasDatabaseName("IX_AuditEvents_UsuarioId");
        builder.HasIndex(e => new { e.EntityName, e.EntityId }).HasDatabaseName("IX_AuditEvents_Entity");
        builder.HasIndex(e => e.CorrelationId).HasDatabaseName("IX_AuditEvents_CorrelationId");
        builder.HasIndex(e => e.TimestampUtc).HasDatabaseName("IX_AuditEvents_TimestampUtc");
    }
}
