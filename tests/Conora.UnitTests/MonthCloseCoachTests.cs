using Conora.Domain.Enums;
using Conora.Services;
using Xunit;

namespace Conora.UnitTests;

public class MonthCloseCoachTests
{
    private static MonthCloseCoach.Line Line(
        string name,
        decimal planned,
        decimal actual,
        Guid? id = null,
        CategorySection section = CategorySection.Essential)
        => new(id ?? Guid.NewGuid(), name, section, planned, actual, IsGroup: false);

    [Fact]
    public void Pick_prefers_overrun_with_concrete_amount()
    {
        var ifood = Guid.NewGuid();
        var insight = MonthCloseCoach.Pick(
            "Novembro/2026",
            [
                Line("iFood", 200m, 450m, ifood),
                Line("Moradia", 800m, 0m)
            ]);

        Assert.NotNull(insight);
        Assert.Equal(MonthCloseCoach.Kind.Overrun, insight!.Kind);
        Assert.Equal(ifood, insight.CategoryId);
        Assert.Contains("iFood", insight.Message);
        Assert.Contains("250", insight.Message);
        Assert.Contains("passou", insight.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pick_unposted_when_no_overrun()
    {
        var moradia = Guid.NewGuid();
        var insight = MonthCloseCoach.Pick(
            "Novembro/2026",
            [
                Line("Mercado", 500m, 480m),
                Line("Moradia", 1_200m, 0m, moradia)
            ]);

        Assert.NotNull(insight);
        Assert.Equal(MonthCloseCoach.Kind.Unposted, insight!.Kind);
        Assert.Equal(moradia, insight.CategoryId);
        Assert.Contains("Moradia", insight.Message);
        Assert.Contains("1.200", insight.Message);
        Assert.Contains("zero lançamento", insight.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pick_returns_null_when_nothing_relevant()
    {
        var insight = MonthCloseCoach.Pick(
            "Novembro/2026",
            [
                Line("Mercado", 500m, 400m),
                Line("Moradia", 1_000m, 950m)
            ]);

        Assert.Null(insight);
    }

    [Fact]
    public void Pick_anomaly_when_spend_jumps_vs_previous_month()
    {
        var lazer = Guid.NewGuid();
        var previous = new Dictionary<Guid, decimal> { [lazer] = 100m };
        var insight = MonthCloseCoach.Pick(
            "Novembro/2026",
            [Line("Lazer", 400m, 280m, lazer)],
            previous);

        Assert.NotNull(insight);
        Assert.Equal(MonthCloseCoach.Kind.Anomaly, insight!.Kind);
        Assert.Contains("Lazer", insight.Message);
        Assert.Contains("180", insight.Message);
    }

    [Fact]
    public void Coach_is_context_only_debt_staging_stays_separate()
    {
        // Hierarchy: PendingDebtService.StageAsync is the only mandatory action (user-selected debts).
        // Coach never emits a debt code or stages PendingDebt — it only returns conversational context.
        var insight = MonthCloseCoach.Pick(
            "Novembro/2026",
            [Line("Condomínio", 650m, 0m)]);

        Assert.NotNull(insight);
        Assert.Equal(MonthCloseCoach.Kind.Unposted, insight!.Kind);
        Assert.Equal("MONTH_CLOSE_COACH", MonthCloseCoach.AlertCode);
        Assert.NotEqual("PENDING_DEBT", MonthCloseCoach.AlertCode);
        Assert.DoesNotContain("dívida", insight.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PendingDebt", insight.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPreviewSummary_within_budget_with_overrun_category()
    {
        var text = MonthCloseCoach.BuildPreviewSummary(
            "Novembro/2026",
            totalPlanned: 5_000m,
            totalActual: 4_800m,
            lines:
            [
                Line("Mercado", 1_000m, 900m),
                Line("iFood", 200m, 350m),
                Line("Moradia", 2_000m, 2_000m)
            ],
            openGapCount: 1,
            openGapTotal: 100m);

        Assert.Contains("segurou bem o orçamento", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("iFood", text);
        Assert.Contains("dívida", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("R$", text);
        Assert.DoesNotContain('\n', text);
    }

    [Fact]
    public void BuildPreviewSummary_over_budget_names_main_slip()
    {
        var text = MonthCloseCoach.BuildPreviewSummary(
            "Outubro/2026",
            totalPlanned: 3_000m,
            totalActual: 3_800m,
            lines: [Line("Delivery", 300m, 900m)],
            openGapCount: 0,
            openGapTotal: 0m);

        Assert.Contains("acima do planejado", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Delivery", text);
        Assert.Contains("pode fechar", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPreviewSummary_no_insight_stays_one_paragraph()
    {
        var text = MonthCloseCoach.BuildPreviewSummary(
            "Setembro/2026",
            totalPlanned: 2_000m,
            totalActual: 1_500m,
            lines: [Line("Mercado", 500m, 400m)],
            openGapCount: 0,
            openGapTotal: 0m);

        Assert.Contains("fechou redondo", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\n', text);
    }
}
