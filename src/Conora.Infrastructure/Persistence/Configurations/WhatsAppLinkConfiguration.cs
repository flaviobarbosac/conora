using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class WhatsAppLinkConfiguration : IEntityTypeConfiguration<WhatsAppLink>
{
    public void Configure(EntityTypeBuilder<WhatsAppLink> builder)
    {
        builder.ToTable("whatsapp_links");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.PhoneE164).IsRequired().HasMaxLength(20);
        builder.HasIndex(e => e.PhoneE164)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_whatsapp_links_Phone");
        builder.HasIndex(e => e.UsuarioId);
    }
}
