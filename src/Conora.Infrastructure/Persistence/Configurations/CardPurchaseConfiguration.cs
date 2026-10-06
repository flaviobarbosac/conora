using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class CardPurchaseConfiguration : IEntityTypeConfiguration<CardPurchase>
{
    public void Configure(EntityTypeBuilder<CardPurchase> builder)
    {
        builder.ToTable("card_purchases");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Description).IsRequired().HasMaxLength(250);
        builder.Property(e => e.CompetenceYm).IsRequired().HasMaxLength(7);
        builder.Property(e => e.FirstInvoiceYm).IsRequired().HasMaxLength(7);
        builder.HasIndex(e => new { e.UsuarioId, e.CompetenceYm });
        builder.HasIndex(e => new { e.UsuarioId, e.CreditCardId });
    }
}
