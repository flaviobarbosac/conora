using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class EntryConfiguration : IEntityTypeConfiguration<Entry>
{
    public void Configure(EntityTypeBuilder<Entry> builder)
    {
        builder.ToTable("entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.CompetenceYm).IsRequired().HasMaxLength(7);
        builder.Property(e => e.Description).IsRequired().HasMaxLength(250);
        builder.Property(e => e.RecurrenceKey).HasMaxLength(60);
        builder.Property(e => e.ImportHash).HasMaxLength(64);
        builder.Ignore(e => e.AffectsMonthlyResult);

        builder.HasIndex(e => new { e.UsuarioId, e.CompetenceYm });
        builder.HasIndex(e => new { e.UsuarioId, e.OccurredAt });
        builder.HasIndex(e => new { e.UsuarioId, e.ImportHash });
        builder.HasIndex(e => new { e.UsuarioId, e.ChartAccountId });
    }
}
