using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class DevicePushTokenConfiguration : IEntityTypeConfiguration<DevicePushToken>
{
    public void Configure(EntityTypeBuilder<DevicePushToken> builder)
    {
        builder.ToTable("device_push_tokens");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.Token).IsRequired().HasMaxLength(4096);
        builder.Property(e => e.Platform).IsRequired().HasMaxLength(16);
        builder.Property(e => e.LastSeenAt).IsRequired();
        builder.HasIndex(e => e.Token)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_device_push_tokens_Token");
        builder.HasIndex(e => e.UsuarioId);
    }
}
