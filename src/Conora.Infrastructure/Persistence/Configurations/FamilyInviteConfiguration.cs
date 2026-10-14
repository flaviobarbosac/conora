using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class FamilyInviteConfiguration : IEntityTypeConfiguration<FamilyInvite>
{
    public void Configure(EntityTypeBuilder<FamilyInvite> builder)
    {
        builder.ToTable("family_invites");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Email).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Token).IsRequired().HasMaxLength(128);
        builder.HasIndex(e => e.Email);
        builder.HasIndex(e => e.InviterUsuarioId);
        builder.HasIndex(e => e.Token).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.InviterUsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FamilyGroup>().WithMany().HasForeignKey(e => e.FamilyGroupId).OnDelete(DeleteBehavior.SetNull);
    }
}
