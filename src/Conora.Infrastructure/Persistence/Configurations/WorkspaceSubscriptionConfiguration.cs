using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class WorkspaceSubscriptionConfiguration : IEntityTypeConfiguration<WorkspaceSubscription>
{
    public void Configure(EntityTypeBuilder<WorkspaceSubscription> builder)
    {
        builder.ToTable("workspace_subscriptions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.Plan).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(e => e.UsuarioId)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_workspace_subscriptions_UsuarioId");
        builder.Property(e => e.Store).HasMaxLength(20);
        builder.Property(e => e.StoreProductId).HasMaxLength(100);
        builder.Property(e => e.StoreSubscriptionId).HasMaxLength(512);
        // One store subscription pays for one Conora login.
        builder.HasIndex(e => new { e.Store, e.StoreSubscriptionId })
            .IsUnique()
            .HasFilter("\"StoreSubscriptionId\" IS NOT NULL AND \"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_workspace_subscriptions_StoreSubscriptionId");
    }
}
