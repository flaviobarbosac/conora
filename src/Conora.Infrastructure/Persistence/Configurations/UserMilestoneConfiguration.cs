using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class UserMilestoneConfiguration : IEntityTypeConfiguration<UserMilestone>
{
    public void Configure(EntityTypeBuilder<UserMilestone> builder)
    {
        builder.ToTable("user_milestones");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.Key).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Age).IsRequired();
        builder.Property(e => e.Points).IsRequired();
        builder.Property(e => e.GrantedAt).IsRequired();
        builder.HasIndex(e => new { e.UsuarioId, e.Key })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_user_milestones_Usuario_Key");
    }
}
