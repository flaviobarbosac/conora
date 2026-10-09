using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class WhatsAppDraftConfiguration : IEntityTypeConfiguration<WhatsAppDraft>
{
    public void Configure(EntityTypeBuilder<WhatsAppDraft> builder)
    {
        builder.ToTable("whatsapp_drafts");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.PhoneE164).IsRequired().HasMaxLength(20);
        builder.Property(e => e.PayloadJson).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(e => new { e.UsuarioId, e.Status });
    }
}
