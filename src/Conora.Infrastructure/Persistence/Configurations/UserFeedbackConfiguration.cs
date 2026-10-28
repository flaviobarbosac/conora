using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class UserFeedbackConfiguration : IEntityTypeConfiguration<UserFeedback>
{
    public void Configure(EntityTypeBuilder<UserFeedback> builder)
    {
        builder.ToTable("user_feedbacks");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.UsuarioNome).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Modulo).IsRequired().HasMaxLength(80);
        builder.Property(e => e.Rota).HasMaxLength(200);
        builder.Property(e => e.Nota).IsRequired();
        builder.Property(e => e.Comentario).IsRequired().HasMaxLength(2000);
        builder.Property(e => e.Status).IsRequired().HasConversion<int>();
        builder.Property(e => e.LidoPorUsuarioNome).HasMaxLength(200);
        builder.Property(e => e.Resposta).HasMaxLength(2000);
        builder.Property(e => e.RespondidoPorUsuarioNome).HasMaxLength(200);
        builder.HasIndex(e => new { e.UsuarioId, e.CreatedAt });
        builder.HasIndex(e => e.Status);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}
