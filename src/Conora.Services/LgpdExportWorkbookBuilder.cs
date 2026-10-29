using System.Globalization;
using System.Reflection;
using ClosedXML.Excel;
using Conora.Domain.Entities;

namespace Conora.Services;

/// <summary>
/// Builds one .xlsx with a sheet per entity. Logo in A1; currency/date columns formatted.
/// </summary>
public static class LgpdExportWorkbookBuilder
{
    private static readonly HashSet<string> SecretNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash", "PasswordResetTokenHash", "EmailVerificationTokenHash",
        "TokenHash", "DownloadTokenHash", "ImportHash"
    };

    private static readonly string[] MoneyHints =
    [
        "Amount", "Balance", "Planned", "Goal", "Accumulated", "Price", "Value", "Total"
    ];

    public static byte[] Build(
        IReadOnlyList<(string SheetName, Type EntityType, IReadOnlyList<object> Rows)> sheets,
        byte[]? logoPng)
    {
        using var workbook = new XLWorkbook();
        if (sheets.Count == 0)
        {
            var empty = workbook.AddWorksheet("Dados");
            PlaceLogo(empty, logoPng);
            empty.Cell(3, 1).Value = "Nenhum dado encontrado.";
        }
        else
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (sheetName, entityType, rows) in sheets)
            {
                var unique = SanitizeSheetName(sheetName);
                var baseName = unique;
                var n = 2;
                while (!used.Add(unique))
                    unique = SanitizeSheetName($"{baseName}_{n++}");
                WriteSheet(workbook, unique, entityType, rows, logoPng);
            }
        }

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static void WriteSheet(
        XLWorkbook workbook,
        string sheetName,
        Type entityType,
        IReadOnlyList<object> rows,
        byte[]? logoPng)
    {
        var ws = workbook.Worksheets.Add(sheetName);
        PlaceLogo(ws, logoPng);

        var props = entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Where(p => !SecretNames.Contains(p.Name))
            .Where(p => IsExportableType(p.PropertyType))
            .ToList();

        // Header row under the logo
        const int headerRow = 3;
        for (var c = 0; c < props.Count; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = props[c].Name;
            cell.Style.Font.Bold = true;
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (var c = 0; c < props.Count; c++)
            {
                var prop = props[c];
                var raw = prop.GetValue(row);
                var cell = ws.Cell(headerRow + 1 + r, c + 1);
                WriteValue(cell, prop, raw);
            }
        }

        if (props.Count > 0)
            ws.Columns(1, props.Count).AdjustToContents(headerRow, headerRow + Math.Max(rows.Count, 1));
        ws.Row(1).Height = 48;
    }

    private static void WriteValue(IXLCell cell, PropertyInfo prop, object? raw)
    {
        if (raw is null)
        {
            cell.Clear();
            return;
        }

        switch (raw)
        {
            case DateTime dt:
                cell.Value = dt;
                cell.Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
                break;
            case decimal dec:
                cell.Value = dec;
                cell.Style.NumberFormat.Format = LooksLikeMoney(prop.Name) ? "R$ #,##0.00" : "#,##0.00";
                break;
            case bool b:
                cell.Value = b ? "Sim" : "Não";
                break;
            case Enum e:
                cell.Value = e.ToString();
                break;
            case Guid g:
                cell.Value = g.ToString("D");
                break;
            case int i:
                cell.Value = i;
                break;
            case long l:
                cell.Value = l;
                break;
            default:
                cell.Value = Convert.ToString(raw, CultureInfo.GetCultureInfo("pt-BR")) ?? "";
                break;
        }
    }

    private static bool IsExportableType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(string)
               || t.IsEnum
               || t == typeof(bool)
               || t == typeof(int)
               || t == typeof(long)
               || t == typeof(decimal)
               || t == typeof(Guid)
               || t == typeof(DateTime);
    }

    private static bool LooksLikeMoney(string name)
        => MoneyHints.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase));

    private static void PlaceLogo(IXLWorksheet ws, byte[]? logoPng)
    {
        ws.Cell(1, 1).Value = "Conora";
        ws.Cell(1, 1).Style.Font.Bold = true;
        if (logoPng is null || logoPng.Length == 0) return;
        using var stream = new MemoryStream(logoPng);
        ws.AddPicture(stream)
            .MoveTo(ws.Cell(1, 1))
            .WithSize(140, 40);
    }

    private static string SanitizeSheetName(string name)
    {
        var cleaned = string.Join("", name.Select(ch =>
            ch is '<' or '>' or ':' or '"' or '/' or '\\' or '?' or '*' or '[' or ']' ? '_' : ch));
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "Sheet";
        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }

    /// <summary>Friendly sheet titles for known entities.</summary>
    public static string SheetTitle(Type type) => type.Name switch
    {
        nameof(User) => "Dados gerais",
        nameof(Budget) => "Orcamento",
        nameof(BudgetLine) => "Orcamento linhas",
        nameof(Entry) => "Lancamentos",
        nameof(LifeProject) => "Projetos de vida",
        nameof(PatrimonyItem) => "Patrimonio",
        nameof(Category) => "Categorias",
        nameof(Account) => "Contas",
        nameof(CreditCard) => "Cartoes",
        nameof(CardPurchase) => "Compras cartao",
        nameof(CardInvoice) => "Faturas",
        nameof(IncomeSource) => "Fontes de renda",
        nameof(FamilyMember) => "Membros",
        nameof(FamilyGroup) => "Grupo familiar",
        nameof(FamilyGroupMember) => "Membros do grupo",
        nameof(FamilyInvite) => "Convites",
        nameof(FamilyNotice) => "Avisos familia",
        nameof(FamilyOwnerTransfer) => "Transferencias titular",
        nameof(MonthLock) => "Fechamento mes",
        nameof(WhatsAppLink) => "WhatsApp vinculo",
        nameof(WhatsAppDraft) => "WhatsApp rascunhos",
        nameof(ReceiptIntake) => "Recibos",
        nameof(ImportBatch) => "Importacoes",
        nameof(ImportPreviewRow) => "Importacao linhas",
        nameof(AuditEvent) => "Auditoria",
        nameof(LgpdRequest) => "Pedidos LGPD",
        nameof(RefreshToken) => "Sessoes",
        nameof(UserMilestone) => "Marcos",
        nameof(AlertSeen) => "Alertas vistos",
        nameof(WorkspaceSubscription) => "Assinatura",
        nameof(UserFeedback) => "Feedback",
        nameof(SupplierUserCategory) => "Fornecedor categoria",
        nameof(SupplierUserBankAccount) => "Fornecedor conta",
        nameof(HotmartCoursePurchase) => "Hotmart",
        _ => type.Name
    };
}
