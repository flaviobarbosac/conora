using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Xunit;

namespace Conora.UnitTests;

public class EntityCoverageTests
{
    [Fact]
    public void Category_analytical_lifecycle()
    {
        var parentId = Guid.NewGuid();
        var cat = Category.CreateAnalytical("Mercado", parentId, CategorySection.Essential, 1);
        Assert.True(cat.AcceptsPosting);
        cat.Rename("Supermercado");
        Assert.Equal("Supermercado", cat.Name);
        cat.SetActive(false);
        Assert.False(cat.AcceptsPosting);
        cat.SetActive(true);
        cat.SetSharingScope(SharingScope.Personal);
        Assert.Equal(SharingScope.Personal, cat.SharingScope);
        cat.MarkFamily();
        Assert.Equal(SharingScope.Family, cat.SharingScope);
        cat.AssignFamilyIdentity(Guid.NewGuid(), Guid.NewGuid());
        Assert.NotNull(cat.FamilyKey);
        cat.MarkPendingReview();
        Assert.True(cat.IsPendingReview);
        Assert.False(cat.AcceptsPosting);
        cat.AcceptReview();
        Assert.False(cat.IsPendingReview);
        cat.Reparent(Guid.NewGuid(), CategorySection.Social);
        Assert.Equal(CategorySection.Social, cat.Section);
    }

    [Fact]
    public void Category_system_guards()
    {
        var system = Category.CreateSystem(SystemCategories.All.First(c => c.Code == "INC_SALARY"), Guid.NewGuid());
        Assert.Throws<SystemCategoryProtectedException>(() => system.Rename("X"));
    }

    [Fact]
    public void Entry_create_and_balance_effects_for_expense()
    {
        var accountId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var entry = Entry.Create(
            EntryType.Expense,
            amount: 40m,
            occurredAt: new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc),
            description: "Teste",
            competenceYm: "2026-10",
            accountId: accountId,
            categoryId: categoryId);

        Assert.Equal(40m, entry.Amount);
        var effects = entry.BalanceEffects().ToList();
        Assert.Contains(effects, e => e.AccountId == accountId && e.Delta < 0);
    }

    [Fact]
    public void Budget_and_line_basics()
    {
        var budget = Budget.Create("2026-10", BudgetMode.Simple);
        Assert.Equal("2026-10", budget.CompetenceYm);
        budget.SetMode(BudgetMode.Detailed);
        Assert.Equal(BudgetMode.Detailed, budget.Mode);
        var line = BudgetLine.Create(budget.Id, Guid.NewGuid(), 250m);
        Assert.Equal(250m, line.PlannedAmount);
        line.SetPlanned(300m);
        Assert.Equal(300m, line.PlannedAmount);
    }

    [Fact]
    public void LifeProject_and_patrimony_create()
    {
        var project = LifeProject.Create(
            name: "Viagem",
            goalAmount: 10000m,
            dueDate: new DateTime(2027, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            contributionStartYm: "2026-11",
            categoryId: Guid.NewGuid(),
            scope: LifeProjectScope.Personal);
        Assert.Equal("Viagem", project.Name);
        project.ApplyContribution(100m);
        Assert.Equal(100m, project.AccumulatedAmount);

        var item = PatrimonyItem.Create(Guid.NewGuid(), "Carro", 50000m);
        Assert.Equal(50000m, item.Amount);
        item.SetAmount(48000m);
        Assert.Equal(48000m, item.Amount);
    }
}
