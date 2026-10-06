using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class LgpdRequestConfiguration : IEntityTypeConfiguration<LgpdRequest>
{
    public void Configure(EntityTypeBuilder<LgpdRequest> builder)
    {
        builder.ToTable("lgpd_requests");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.Kind).IsRequired().HasMaxLength(40);
        builder.Property(e => e.RequestedAtUtc).IsRequired();
        builder.HasIndex(e => new { e.UsuarioId, e.Kind, e.RequestedAtUtc });
    }
}
