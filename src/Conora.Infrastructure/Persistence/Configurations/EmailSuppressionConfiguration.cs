using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class EmailSuppressionConfiguration : IEntityTypeConfiguration<EmailSuppression>
{
    public void Configure(EntityTypeBuilder<EmailSuppression> builder)
    {
        builder.ToTable("email_suppressions");
        builder.HasKey(e => e.Email);
        builder.Property(e => e.Email).HasMaxLength(320);
        builder.Property(e => e.Reason).IsRequired().HasMaxLength(40);
        builder.Property(e => e.SourceMessageId).HasMaxLength(200);
        builder.Property(e => e.CreatedAtUtc).IsRequired();
    }
}
