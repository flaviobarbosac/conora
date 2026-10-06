using Conora.Domain.Enums;

namespace Conora.Domain.Catalog;

public sealed record SystemCategoryDefinition(string Code, string Name, CategoryKind Kind, bool IsEssential);

/// <summary>System categories seeded for every tenant. "Descontos sobre renda" is intentionally not launchable (spec v1.1).</summary>
public static class SystemCategories
{
    public const string Contributions = "CONTRIBUTIONS";
    public const string Transfer = "TRANSFER";

    public static readonly IReadOnlyList<SystemCategoryDefinition> All =
    [
        new("HOUSING", "Moradia", CategoryKind.Expense, true),
        new("FOOD", "Alimentação", CategoryKind.Expense, true),
        new("TRANSPORT", "Transporte", CategoryKind.Expense, true),
        new("HEALTH", "Saúde", CategoryKind.Expense, true),
        new("EDUCATION", "Educação", CategoryKind.Expense, true),
        new("DEBTS", "Dívidas e financiamentos", CategoryKind.Expense, true),
        new("LEISURE", "Lazer", CategoryKind.Expense, false),
        new("CLOTHING", "Vestuário", CategoryKind.Expense, false),
        new(Contributions, "Contribuições/Doações", CategoryKind.Expense, false),
        new("OTHER_EXPENSE", "Outras despesas", CategoryKind.Expense, false),
        new("SALARY", "Salário", CategoryKind.Income, false),
        new("EXTRA_INCOME", "Renda extra", CategoryKind.Income, false),
        new("INVESTMENT_INCOME", "Rendimentos", CategoryKind.Income, false),
        new(Transfer, "Transferência", CategoryKind.Transfer, false)
    ];

    public static bool IsForbiddenName(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return normalized.StartsWith("descontos sobre renda", StringComparison.Ordinal)
               || normalized.StartsWith("desconto sobre renda", StringComparison.Ordinal);
    }
}
