using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class CardInvoiceConfiguration : IEntityTypeConfiguration<CardInvoice>
{
    public void Configure(EntityTypeBuilder<CardInvoice> builder)
    {
        builder.ToTable("card_invoices");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.CompetenceYm).IsRequired().HasMaxLength(7);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Total).HasPrecision(18, 2);
        builder.HasIndex(e => new { e.UsuarioId, e.CreditCardId, e.CompetenceYm })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_card_invoices_Card_Competence");
    }
}
