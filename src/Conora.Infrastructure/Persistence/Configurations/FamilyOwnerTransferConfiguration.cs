using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class FamilyOwnerTransferConfiguration : IEntityTypeConfiguration<FamilyOwnerTransfer>
{
    public void Configure(EntityTypeBuilder<FamilyOwnerTransfer> builder)
    {
        builder.ToTable("family_owner_transfers");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.FamilyGroupId).IsRequired();
        builder.Property(e => e.FromUsuarioId).IsRequired();
        builder.Property(e => e.ToUsuarioId).IsRequired();
        builder.Property(e => e.Token).IsRequired().HasMaxLength(64);
        builder.HasIndex(e => e.Token).IsUnique();
        builder.HasIndex(e => new { e.FamilyGroupId, e.AcceptedAt, e.CancelledAt });
        builder.HasIndex(e => e.FamilyGroupId)
            .IsUnique()
            .HasDatabaseName("IX_family_owner_transfers_one_open")
            .HasFilter("\"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NULL AND \"DeletedAt\" IS NULL");
        builder.HasOne<FamilyGroup>().WithMany().HasForeignKey(e => e.FamilyGroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.FromUsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ToUsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}
