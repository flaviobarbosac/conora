using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class FamilyGroupConfiguration : IEntityTypeConfiguration<FamilyGroup>
{
    public void Configure(EntityTypeBuilder<FamilyGroup> builder)
    {
        builder.ToTable("family_groups");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.CreatedByUsuarioId).IsRequired();
    }
}

public class FamilyGroupMemberConfiguration : IEntityTypeConfiguration<FamilyGroupMember>
{
    public void Configure(EntityTypeBuilder<FamilyGroupMember> builder)
    {
        builder.ToTable("family_group_members");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.FamilyGroupId).IsRequired();
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.HasIndex(e => e.UsuarioId)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_family_group_members_UsuarioId_active");
        builder.HasIndex(e => e.FamilyGroupId);
    }
}

public class FamilyInviteConfiguration : IEntityTypeConfiguration<FamilyInvite>
{
    public void Configure(EntityTypeBuilder<FamilyInvite> builder)
    {
        builder.ToTable("family_invites");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Email).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Token).IsRequired().HasMaxLength(128);
        builder.HasIndex(e => e.Token).IsUnique();
        builder.HasIndex(e => e.InviterUsuarioId);
        builder.HasIndex(e => e.Email);
    }
}

public class FamilyNoticeConfiguration : IEntityTypeConfiguration<FamilyNotice>
{
    public void Configure(EntityTypeBuilder<FamilyNotice> builder)
    {
        builder.ToTable("family_notices");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Kind).IsRequired().HasMaxLength(60);
        builder.Property(e => e.Message).IsRequired().HasMaxLength(500);
        builder.HasIndex(e => new { e.UsuarioId, e.ReadAt });
    }
}
