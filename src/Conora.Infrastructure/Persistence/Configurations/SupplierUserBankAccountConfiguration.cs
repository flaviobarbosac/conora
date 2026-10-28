using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class SupplierUserBankAccountConfiguration : IEntityTypeConfiguration<SupplierUserBankAccount>
{
    public void Configure(EntityTypeBuilder<SupplierUserBankAccount> builder)
    {
        builder.ToTable("supplier_user_bank_accounts");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.SupplierId, e.UsuarioId }).IsUnique();
        builder.HasQueryFilter(e => e.DeletedAt == null);
    }
}
