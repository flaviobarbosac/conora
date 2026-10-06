using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Conora.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public const string ArchitectureSchema = "architecture";

    private readonly ITenantContext? _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenant = null) : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LgpdRequest> LgpdRequests => Set<LgpdRequest>();

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
    public DbSet<WorkspaceSubscription> WorkspaceSubscriptions => Set<WorkspaceSubscription>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportPreviewRow> ImportPreviewRows => Set<ImportPreviewRow>();
    public DbSet<WhatsAppLink> WhatsAppLinks => Set<WhatsAppLink>();
    public DbSet<WhatsAppDraft> WhatsAppDrafts => Set<WhatsAppDraft>();

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
        TenantFilter<ImportBatch>(modelBuilder);
        TenantFilter<ImportPreviewRow>(modelBuilder);
        TenantFilter<WhatsAppLink>(modelBuilder);
        TenantFilter<WhatsAppDraft>(modelBuilder);

        modelBuilder.AddTransactionalOutboxEntities();
        modelBuilder.Entity<InboxState>().ToTable("InboxState", ArchitectureSchema);
        modelBuilder.Entity<OutboxMessage>().ToTable("OutboxMessage", ArchitectureSchema);
        modelBuilder.Entity<OutboxState>().ToTable("OutboxState", ArchitectureSchema);
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
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>().Where(e => e.State == EntityState.Added))
        {
            if (_tenant?.UsuarioId is not Guid tid)
                throw new UnauthorizedAccessException("Tenant não autenticado para carimbar UsuarioId.");

            if (entry.Entity.UsuarioId != Guid.Empty && entry.Entity.UsuarioId != tid)
                throw new UnauthorizedAccessException("Operação fora do tenant autenticado.");

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
