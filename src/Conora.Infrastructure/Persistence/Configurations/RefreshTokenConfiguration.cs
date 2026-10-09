using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.TokenHash).IsRequired().HasMaxLength(128);
        builder.Property(e => e.ExpiresAtUtc).IsRequired();
        builder.HasIndex(e => e.TokenHash);
        builder.HasIndex(e => e.UsuarioId);
    }
}
