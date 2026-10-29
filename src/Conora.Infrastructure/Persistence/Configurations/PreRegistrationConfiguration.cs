using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class PreRegistrationConfiguration : IEntityTypeConfiguration<PreRegistration>
{
    public void Configure(EntityTypeBuilder<PreRegistration> builder)
    {
        builder.ToTable("pre_registrations");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Email).IsRequired().HasMaxLength(320);
        builder.Property(e => e.Cpf).HasMaxLength(11);
        builder.Property(e => e.PasswordHash).HasMaxLength(500);
        builder.Property(e => e.GoogleId).HasMaxLength(100);
        builder.Property(e => e.AppleId).HasMaxLength(100);
        builder.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.ExpiresAtUtc).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.IsTrainingStudent).IsRequired();

        builder.HasIndex(e => e.Email)
            .IsUnique()
            .HasDatabaseName("IX_PreRegistrations_Email");

        builder.HasIndex(e => e.TokenHash)
            .IsUnique()
            .HasDatabaseName("IX_PreRegistrations_TokenHash");

        builder.HasIndex(e => e.GoogleId)
            .IsUnique()
            .HasFilter("\"GoogleId\" IS NOT NULL")
            .HasDatabaseName("IX_PreRegistrations_GoogleId");

        builder.HasIndex(e => e.AppleId)
            .IsUnique()
            .HasFilter("\"AppleId\" IS NOT NULL")
            .HasDatabaseName("IX_PreRegistrations_AppleId");

        builder.HasIndex(e => e.ExpiresAtUtc)
            .HasDatabaseName("IX_PreRegistrations_ExpiresAtUtc");
    }
}
