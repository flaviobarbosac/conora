using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class ReceiptIntakeConfiguration : IEntityTypeConfiguration<ReceiptIntake>
{
    public void Configure(EntityTypeBuilder<ReceiptIntake> builder)
    {
        builder.ToTable("receipt_intakes");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Purpose).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ImageObjectKey).IsRequired().HasMaxLength(260);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.ExtractedCnpj).HasMaxLength(14);
        builder.Property(e => e.ExtractedName).HasMaxLength(200);
        builder.Property(e => e.ExtractedAmount).HasPrecision(18, 2);
        builder.Property(e => e.ProposedCategoryCode).HasMaxLength(40);
        builder.Property(e => e.PhoneE164).HasMaxLength(20);
        builder.Property(e => e.WhatsAppAwaiting).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.FailureReason).HasMaxLength(500);
        builder.HasIndex(e => new { e.UsuarioId, e.Status });
        builder.HasIndex(e => e.ImageExpiresAt);
        builder.HasIndex(e => new { e.PhoneE164, e.WhatsAppAwaiting });
    }
}
