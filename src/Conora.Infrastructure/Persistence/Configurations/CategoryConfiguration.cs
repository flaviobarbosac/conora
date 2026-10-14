using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(160);
        builder.Property(e => e.Code).HasMaxLength(60);
        builder.Property(e => e.DisplayNumber).HasMaxLength(40);
        builder.Property(e => e.Level).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Section).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.SharingScope)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(SharingScope.Family);
        builder.Property(e => e.IsPendingReview)
            .IsRequired()
            .HasDefaultValue(false);
        builder.Ignore(e => e.AcceptsPosting);
        builder.HasIndex(e => e.FamilyKey)
            .HasFilter("\"FamilyKey\" IS NOT NULL AND \"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_categories_FamilyKey");
        builder.HasIndex(e => new { e.UsuarioId, e.IsPendingReview })
            .HasDatabaseName("IX_categories_UsuarioId_IsPendingReview");

        builder.HasIndex(e => new { e.UsuarioId, e.Code })
            .IsUnique()
            .HasFilter("\"Code\" IS NOT NULL AND \"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_categories_UsuarioId_Code");
        builder.HasIndex(e => new { e.UsuarioId, e.ParentId });
        builder.HasIndex(e => new { e.UsuarioId, e.Section });
        builder.HasIndex(e => new { e.UsuarioId, e.SharingScope })
            .HasDatabaseName("IX_categories_UsuarioId_SharingScope");
    }
}
