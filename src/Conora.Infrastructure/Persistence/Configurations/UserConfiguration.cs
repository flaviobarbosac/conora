using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Name).IsRequired().HasMaxLength(200);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(320);
        builder.Property(u => u.Cpf).HasMaxLength(11);
        builder.Property(u => u.PasswordHash).HasMaxLength(500);
        builder.Property(u => u.PasswordResetTokenHash).HasMaxLength(64);
        builder.Property(u => u.GoogleId).HasMaxLength(100);
        builder.Property(u => u.AppleId).HasMaxLength(100);
        builder.Property(u => u.EmailVerified).IsRequired();
        builder.Property(u => u.OnboardingCompleted).IsRequired();
        builder.Property(u => u.ThemeId).IsRequired().HasMaxLength(40);
        builder.Property(u => u.QuietMode).IsRequired();
        builder.Property(u => u.EmailsEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(u => u.ReserveTargetMonths).IsRequired();
        builder.Property(u => u.MonthCloseDay).IsRequired();
        builder.Property(u => u.IncludeAbsoluteValuesInRetrospective).IsRequired();
        builder.Property(u => u.VoiceEntryConfirm).IsRequired().HasDefaultValue(true);
        builder.Property(u => u.Version).IsRequired();
        builder.Property(u => u.CreatedAt).IsRequired();
        builder.Property(u => u.UpdatedAt).IsRequired();
        builder.Property(u => u.DeletedAt);

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_Users_Email");

        builder.HasIndex(u => u.GoogleId)
            .IsUnique()
            .HasFilter("\"GoogleId\" IS NOT NULL AND \"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_Users_GoogleId");

        builder.HasIndex(u => u.AppleId)
            .IsUnique()
            .HasFilter("\"AppleId\" IS NOT NULL AND \"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_Users_AppleId");

        builder.HasIndex(u => u.Cpf)
            .IsUnique()
            .HasFilter("\"Cpf\" IS NOT NULL AND \"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_Users_Cpf");

        builder.HasIndex(u => u.PasswordResetTokenHash)
            .IsUnique()
            .HasFilter("\"PasswordResetTokenHash\" IS NOT NULL")
            .HasDatabaseName("IX_Users_PasswordResetTokenHash");
    }
}
