using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class ImportPreviewRowConfiguration : IEntityTypeConfiguration<ImportPreviewRow>
{
    public void Configure(EntityTypeBuilder<ImportPreviewRow> builder)
    {
        builder.ToTable("import_preview_rows");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.RawJson).IsRequired();
        builder.Property(e => e.MappedAmount).HasPrecision(18, 2);
        builder.Property(e => e.MappedDescription).IsRequired().HasMaxLength(250);
        builder.Property(e => e.ImportHash).IsRequired().HasMaxLength(64);
        builder.HasIndex(e => new { e.UsuarioId, e.BatchId });
    }
}
