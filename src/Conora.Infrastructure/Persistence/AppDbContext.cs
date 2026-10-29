using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Conora.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    private readonly ITenantContext? _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenant = null) : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<PreRegistration> PreRegistrations => Set<PreRegistration>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<RequestFailure> RequestFailures => Set<RequestFailure>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LgpdRequest> LgpdRequests => Set<LgpdRequest>();
    public DbSet<EmailSuppression> EmailSuppressions => Set<EmailSuppression>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierUserCategory> SupplierUserCategories => Set<SupplierUserCategory>();
    public DbSet<SupplierUserBankAccount> SupplierUserBankAccounts => Set<SupplierUserBankAccount>();
    // Finance domain (all tenant-owned)
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<IncomeSource> IncomeSources => Set<IncomeSource>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<CreditCard> CreditCards => Set<CreditCard>();
    public DbSet<CardPurchase> CardPurchases => Set<CardPurchase>();
    public DbSet<CardInvoice> CardInvoices => Set<CardInvoice>();
    public DbSet<Entry> Entries => Set<Entry>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();
    public DbSet<MonthLock> MonthLocks => Set<MonthLock>();
    public DbSet<LifeProject> LifeProjects => Set<LifeProject>();
    public DbSet<PatrimonyItem> PatrimonyItems => Set<PatrimonyItem>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<FamilyGroup> FamilyGroups => Set<FamilyGroup>();
    public DbSet<FamilyGroupMember> FamilyGroupMembers => Set<FamilyGroupMember>();
    public DbSet<FamilyInvite> FamilyInvites => Set<FamilyInvite>();
    public DbSet<FamilyOwnerTransfer> FamilyOwnerTransfers => Set<FamilyOwnerTransfer>();
    public DbSet<FamilyNotice> FamilyNotices => Set<FamilyNotice>();
    public DbSet<WorkspaceSubscription> WorkspaceSubscriptions => Set<WorkspaceSubscription>();
    public DbSet<UserMilestone> UserMilestones => Set<UserMilestone>();
    public DbSet<AlertSeen> AlertSeen => Set<AlertSeen>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportPreviewRow> ImportPreviewRows => Set<ImportPreviewRow>();
    public DbSet<WhatsAppLink> WhatsAppLinks => Set<WhatsAppLink>();
    public DbSet<WhatsAppDraft> WhatsAppDrafts => Set<WhatsAppDraft>();
    public DbSet<ReceiptIntake> ReceiptIntakes => Set<ReceiptIntake>();
    public DbSet<HotmartCoursePurchase> HotmartCoursePurchases => Set<HotmartCoursePurchase>();
    public DbSet<UserFeedback> UserFeedbacks => Set<UserFeedback>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.Entity<User>()
            .HasQueryFilter(e => e.DeletedAt == null && e.Id == _tenant!.UsuarioId);

        TenantFilter<AuditEvent>(modelBuilder);
        TenantFilter<RefreshToken>(modelBuilder);
        TenantFilter<LgpdRequest>(modelBuilder);
        TenantFilter<Category>(modelBuilder);
        TenantFilter<IncomeSource>(modelBuilder);
        TenantFilter<Account>(modelBuilder);
        TenantFilter<CreditCard>(modelBuilder);
        TenantFilter<CardPurchase>(modelBuilder);
        TenantFilter<CardInvoice>(modelBuilder);
        TenantFilter<Entry>(modelBuilder);
        TenantFilter<Budget>(modelBuilder);
        TenantFilter<BudgetLine>(modelBuilder);
        TenantFilter<MonthLock>(modelBuilder);
        TenantFilter<LifeProject>(modelBuilder);
        TenantFilter<PatrimonyItem>(modelBuilder);
        TenantFilter<FamilyMember>(modelBuilder);
        TenantFilter<WorkspaceSubscription>(modelBuilder);
        TenantFilter<UserMilestone>(modelBuilder);
        TenantFilter<ImportBatch>(modelBuilder);
        TenantFilter<ImportPreviewRow>(modelBuilder);
        TenantFilter<WhatsAppLink>(modelBuilder);
        TenantFilter<WhatsAppDraft>(modelBuilder);
        TenantFilter<ReceiptIntake>(modelBuilder);
        TenantFilter<UserFeedback>(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTenant();
        StampAudit();

        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pgEx)
        {
            throw new UniqueConstraintViolationException(pgEx.ConstraintName ?? pgEx.SqlState ?? "unknown");
        }
    }

    private void StampTenant()
    {
        if (_tenant?.UsuarioId is not Guid tid)
        {
            if (ChangeTracker.Entries<ITenantOwned>().Any(e => e.State is EntityState.Added or EntityState.Modified))
                throw new UnauthorizedAccessException("Tenant não autenticado para carimbar UsuarioId.");
            return;
        }

        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.UsuarioId != Guid.Empty && entry.Entity.UsuarioId != tid)
                    throw new UnauthorizedAccessException("Operação fora do tenant autenticado.");

                entry.Entity.UsuarioId = tid;
                continue;
            }

            if (entry.State != EntityState.Modified)
                continue;

            var original = entry.Property(nameof(ITenantOwned.UsuarioId)).OriginalValue is Guid originalId
                ? originalId
                : entry.Entity.UsuarioId;

            if (entry.Property(nameof(ITenantOwned.UsuarioId)).IsModified && entry.Entity.UsuarioId != original)
                throw new UnauthorizedAccessException("UsuarioId do tenant não pode ser alterado.");

            if (original != tid || entry.Entity.UsuarioId != tid)
                throw new UnauthorizedAccessException("Operação fora do tenant autenticado.");

            entry.Property(nameof(ITenantOwned.UsuarioId)).IsModified = false;
            entry.Entity.UsuarioId = tid;
        }
    }

    private void StampAudit()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<ModelBase>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.Id == Guid.Empty)
                    entry.Entity.Id = Guid.CreateVersion7();
                if (entry.Entity.CreatedAt == default)
                    entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
                if (entry.Entity.Version == 0)
                    entry.Entity.Version = 1;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }

    private void TenantFilter<T>(ModelBuilder modelBuilder) where T : ModelBase, ITenantOwned
        => modelBuilder.Entity<T>()
            .HasQueryFilter(e => e.DeletedAt == null && e.UsuarioId == _tenant!.UsuarioId);
}
