using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class CategoryDisplayNumbersTests
{
    [Fact]
    public void Apply_assigns_hierarchical_numbers()
    {
        var budget = Category.CreateSystem(
            SystemCategories.All.First(c => c.Code == "BUDGET"),
            parentId: null);
        var income = Category.CreateSystem(
            SystemCategories.All.First(c => c.Code == "INC"),
            parentId: budget.Id);
        // Force parent link for tree assignment
        income.SyncFromDefinition(
            SystemCategories.All.First(c => c.Code == "INC"),
            budget.Id);

        var salaryDef = SystemCategories.All.First(c => c.Code == "INC_SALARY");
        var salary = Category.CreateSystem(salaryDef, income.Id);

        CategoryDisplayNumbers.Apply([budget, income, salary]);

        Assert.False(string.IsNullOrWhiteSpace(budget.DisplayNumber));
        Assert.StartsWith(budget.DisplayNumber + ".", income.DisplayNumber);
        Assert.StartsWith(income.DisplayNumber + ".", salary.DisplayNumber);
    }
}
