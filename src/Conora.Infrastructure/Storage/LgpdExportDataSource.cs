using Conora.Domain.Entities;
using Conora.Domain.Ports;
using Conora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Conora.Infrastructure.Storage;

public sealed class LgpdExportDataSource : ILgpdExportDataSource
{
    private readonly AppDbContext _db;

    public LgpdExportDataSource(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<(string SheetName, Type EntityType, IReadOnlyList<object> Rows)>> LoadAsync(
        Guid usuarioId,
        string userEmail,
        CancellationToken ct = default)
    {
        var sheets = new List<(string, Type, IReadOnlyList<object>)>();

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == usuarioId && u.DeletedAt == null, ct);
        if (user is not null)
            sheets.Add(("Dados gerais", typeof(User), new object[] { user }));

        await AddTenantAsync<Account>("Contas", sheets, ct);
        await AddTenantAsync<Category>("Categorias", sheets, ct);
        await AddTenantAsync<IncomeSource>("Fontes de renda", sheets, ct);
        await AddTenantAsync<Budget>("Orcamento", sheets, ct);
        await AddTenantAsync<BudgetLine>("Orcamento linhas", sheets, ct);
        await AddTenantAsync<Entry>("Lancamentos", sheets, ct);
        await AddTenantAsync<LifeProject>("Projetos de vida", sheets, ct);
        await AddTenantAsync<PatrimonyItem>("Patrimonio", sheets, ct);
        await AddTenantAsync<CreditCard>("Cartoes", sheets, ct);
        await AddTenantAsync<CardPurchase>("Compras cartao", sheets, ct);
        await AddTenantAsync<CardInvoice>("Faturas", sheets, ct);
        await AddTenantAsync<MonthLock>("Fechamento mes", sheets, ct);
        await AddTenantAsync<FamilyMember>("Membros", sheets, ct);
        await AddTenantAsync<WhatsAppLink>("WhatsApp vinculo", sheets, ct);
        await AddTenantAsync<WhatsAppDraft>("WhatsApp rascunhos", sheets, ct);
        await AddTenantAsync<ReceiptIntake>("Recibos", sheets, ct);
        await AddTenantAsync<ImportBatch>("Importacoes", sheets, ct);
        await AddTenantAsync<ImportPreviewRow>("Importacao linhas", sheets, ct);
        await AddTenantAsync<AuditEvent>("Auditoria", sheets, ct);
        await AddTenantAsync<LgpdRequest>("Pedidos LGPD", sheets, ct);
        await AddTenantAsync<RefreshToken>("Sessoes", sheets, ct);
        await AddTenantAsync<UserMilestone>("Marcos", sheets, ct);
        await AddTenantAsync<AlertSeen>("Alertas vistos", sheets, ct);
        await AddTenantAsync<WorkspaceSubscription>("Assinatura", sheets, ct);
        await AddTenantAsync<UserFeedback>("Feedback", sheets, ct);

        var groupIds = await _db.FamilyGroupMembers.AsNoTracking()
            .Where(m => m.UsuarioId == usuarioId && m.DeletedAt == null)
            .Select(m => m.FamilyGroupId)
            .ToListAsync(ct);

        sheets.Add(("Membros do grupo", typeof(FamilyGroupMember),
            await _db.FamilyGroupMembers.AsNoTracking()
                .Where(m => m.UsuarioId == usuarioId && m.DeletedAt == null)
                .Cast<object>()
                .ToListAsync(ct)));

        if (groupIds.Count > 0)
        {
            sheets.Add(("Grupo familiar", typeof(FamilyGroup),
                await _db.FamilyGroups.AsNoTracking()
                    .Where(g => groupIds.Contains(g.Id) && g.DeletedAt == null)
                    .Cast<object>()
                    .ToListAsync(ct)));
        }

        sheets.Add(("Convites", typeof(FamilyInvite),
            await _db.FamilyInvites.AsNoTracking()
                .Where(i => i.DeletedAt == null
                            && (i.InviterUsuarioId == usuarioId || i.AcceptedByUsuarioId == usuarioId))
                .Cast<object>()
                .ToListAsync(ct)));

        sheets.Add(("Avisos familia", typeof(FamilyNotice),
            await _db.FamilyNotices.AsNoTracking()
                .Where(n => n.UsuarioId == usuarioId && n.DeletedAt == null)
                .Cast<object>()
                .ToListAsync(ct)));

        sheets.Add(("Transferencias titular", typeof(FamilyOwnerTransfer),
            await _db.FamilyOwnerTransfers.AsNoTracking()
                .Where(t => t.DeletedAt == null
                            && (t.FromUsuarioId == usuarioId || t.ToUsuarioId == usuarioId))
                .Cast<object>()
                .ToListAsync(ct)));

        sheets.Add(("Fornecedor categoria", typeof(SupplierUserCategory),
            await _db.SupplierUserCategories.AsNoTracking()
                .Where(s => s.UsuarioId == usuarioId && s.DeletedAt == null)
                .Cast<object>()
                .ToListAsync(ct)));

        sheets.Add(("Fornecedor conta", typeof(SupplierUserBankAccount),
            await _db.SupplierUserBankAccounts.AsNoTracking()
                .Where(s => s.UsuarioId == usuarioId && s.DeletedAt == null)
                .Cast<object>()
                .ToListAsync(ct)));

        if (!string.IsNullOrWhiteSpace(userEmail))
        {
            var email = userEmail.Trim().ToLowerInvariant();
            sheets.Add(("Hotmart", typeof(HotmartCoursePurchase),
                await _db.HotmartCoursePurchases.AsNoTracking()
                    .Where(h => h.Email == email)
                    .Cast<object>()
                    .ToListAsync(ct)));
        }

        return sheets;
    }

    private async Task AddTenantAsync<T>(
        string sheetName,
        List<(string, Type, IReadOnlyList<object>)> target,
        CancellationToken ct)
        where T : class, ITenantOwned
    {
        var rows = await _db.Set<T>().AsNoTracking().Cast<object>().ToListAsync(ct);
        target.Add((sheetName, typeof(T), rows));
    }
}
