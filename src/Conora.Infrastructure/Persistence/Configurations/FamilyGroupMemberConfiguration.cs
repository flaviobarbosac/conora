using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class FamilyGroupMemberConfiguration : IEntityTypeConfiguration<FamilyGroupMember>
{
    public void Configure(EntityTypeBuilder<FamilyGroupMember> builder)
    {
        builder.ToTable("family_group_members");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.FamilyGroupId);
        builder.HasIndex(e => e.UsuarioId)
            .IsUnique()
            .HasDatabaseName("IX_family_group_members_UsuarioId_active")
            .HasFilter("\"DeletedAt\" IS NULL");
        builder.HasOne<FamilyGroup>().WithMany().HasForeignKey(e => e.FamilyGroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}
