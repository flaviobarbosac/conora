using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class HotmartCoursePurchaseConfiguration : IEntityTypeConfiguration<HotmartCoursePurchase>
{
    public void Configure(EntityTypeBuilder<HotmartCoursePurchase> builder)
    {
        builder.ToTable("hotmart_course_purchases");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Email).IsRequired().HasMaxLength(320);
        builder.Property(p => p.Transaction).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Approved).IsRequired();
        builder.Property(p => p.ReceivedAt).IsRequired();

        builder.HasIndex(p => p.Transaction)
            .IsUnique()
            .HasDatabaseName("IX_HotmartCoursePurchases_Transaction");

        builder.HasIndex(p => p.Email)
            .HasDatabaseName("IX_HotmartCoursePurchases_Email");
    }
}
