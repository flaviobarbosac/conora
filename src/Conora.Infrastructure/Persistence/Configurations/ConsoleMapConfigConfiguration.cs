using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class ConsoleMapConfigConfiguration : IEntityTypeConfiguration<ConsoleMapConfig>
{
    public void Configure(EntityTypeBuilder<ConsoleMapConfig> builder)
    {
        builder.ToTable("console_map_config");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.PinLabelField).IsRequired().HasMaxLength(40);
        builder.Property(c => c.PinLabelText).HasMaxLength(200);
        builder.Property(c => c.ShowLabel).IsRequired();
        builder.Property(c => c.VisibleFieldsJson).IsRequired();
        builder.Property(c => c.Version).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();
        builder.Property(c => c.DeletedAt);
    }
}
