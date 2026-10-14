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
        builder.Property(e => e.BudgetOwnerUsuarioId).IsRequired();
        builder.HasIndex(e => e.BudgetOwnerUsuarioId);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.CreatedByUsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.BudgetOwnerUsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}
