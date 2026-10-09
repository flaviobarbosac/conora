using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(120);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Balance).HasPrecision(18, 2);
        builder.Property(e => e.BankCode).HasMaxLength(3);
        builder.Property(e => e.Agency).HasMaxLength(20);
        builder.Property(e => e.AccountNumber).HasMaxLength(20);
        builder.Property(e => e.CheckDigit).HasMaxLength(2);
        builder.HasIndex(e => e.UsuarioId);
    }
}
