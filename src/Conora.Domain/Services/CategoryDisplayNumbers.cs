using Conora.Domain.Entities;
using Conora.Domain.Enums;

namespace Conora.Domain.Services;

/// <summary>Assigns hierarchical display numbers (1, 1.1, 2.1) for on-screen identification only.</summary>
public static class CategoryDisplayNumbers
{
    private static readonly CategorySection[] SectionOrder =
    [
        CategorySection.Budget,
        CategorySection.Income,
        CategorySection.Expense,
        CategorySection.Discount,
        CategorySection.LifeProject,
        CategorySection.Essential,
        CategorySection.Social,
        CategorySection.Patrimony,
        CategorySection.Asset,
        CategorySection.Liability
    ];

    public static void Apply(IReadOnlyList<Category> accounts)
    {
        var children = accounts
            .GroupBy(c => c.ParentId ?? Guid.Empty)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList());

        var roots = accounts
            .Where(c => c.ParentId is null)
            .OrderBy(c => Array.IndexOf(SectionOrder, c.Section))
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var index = 1;
        foreach (var root in roots)
        {
            Assign(root, index.ToString(), children);
            index++;
        }
    }

    private static void Assign(
        Category node,
        string number,
        IReadOnlyDictionary<Guid, List<Category>> children)
    {
        node.SetDisplayNumber(number);
        if (!children.TryGetValue(node.Id, out var kids) || kids.Count == 0)
            return;

        var childIndex = 1;
        foreach (var kid in kids)
        {
            Assign(kid, $"{number}.{childIndex}", children);
            childIndex++;
        }
    }
}
