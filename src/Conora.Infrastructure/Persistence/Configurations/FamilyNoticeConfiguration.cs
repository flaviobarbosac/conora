using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class FamilyNoticeConfiguration : IEntityTypeConfiguration<FamilyNotice>
{
    public void Configure(EntityTypeBuilder<FamilyNotice> builder)
    {
        builder.ToTable("family_notices");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Kind).IsRequired().HasMaxLength(60);
        builder.Property(e => e.Message).IsRequired().HasMaxLength(500);
        builder.HasIndex(e => new { e.UsuarioId, e.ReadAt });
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}
