using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class IanQuestionClassifierTests
{
    [Theory]
    [InlineData("me mostre como estamos de despesas este mes")]
    [InlineData("Como estamos de despesas este mês?")]
    [InlineData("onde gastei este mês")]
    [InlineData("mostra meus gastos do mês")]
    public void Expense_month_questions_become_composition_pie(string question)
    {
        var result = IanQuestionClassifier.Classify(question, "2026-10");

        Assert.Equal(IanChartSelector.KindComposition, result.Kind);
        Assert.Equal(IanQuestionClassifier.FocusExpense, result.Focus);
    }

    [Fact]
    public void Whole_month_summary_stays_snapshot()
    {
        var result = IanQuestionClassifier.Classify("Como está meu mês?", "2026-10");

        Assert.Equal(IanChartSelector.KindSnapshot, result.Kind);
        Assert.Null(result.Focus);
    }

    [Fact]
    public void Income_month_questions_become_composition()
    {
        var result = IanQuestionClassifier.Classify("mostre minhas receitas deste mês", "2026-10");

        Assert.Equal(IanChartSelector.KindComposition, result.Kind);
        Assert.Equal(IanQuestionClassifier.FocusIncome, result.Focus);
    }

    [Theory]
    [InlineData("compara receita e despesa este mês")]
    [InlineData("quanto recebi de receita e quanto paguei de despesa")]
    public void Income_vs_expense_becomes_snapshot_pie(string question)
    {
        var result = IanQuestionClassifier.Classify(question, "2026-10");

        Assert.Equal(IanChartSelector.KindSnapshot, result.Kind);
        Assert.Null(result.Focus);
        Assert.Null(result.Subject);
    }

    [Theory]
    [InlineData("Quanto eu recebi na conta \"Recebimento de Salario\" este ano", "Recebimento de Salario")]
    [InlineData("Quanto eu recebi na conta Recebimento de Salario este ano", "Recebimento de Salario")]
    [InlineData("quanto recebi na conta Recebimento de Salário neste ano", "Recebimento de Salário")]
    [InlineData("Evolução da EDP neste ano", "EDP")]
    [InlineData("mostra o que entrei da categoria Salário do ano", "Salário")]
    [InlineData("quanto paguei na conta Energia este ano", "Energia")]
    public void Named_account_or_category_becomes_series(string question, string expectedSubject)
    {
        var result = IanQuestionClassifier.Classify(question, "2026-10");

        Assert.Equal(IanChartSelector.KindSeries, result.Kind);
        Assert.Equal(expectedSubject, result.Subject);
        Assert.Equal("2026-01", result.FromYm);
        Assert.Equal("2026-10", result.ToYm);
    }

    [Theory]
    [InlineData("Recebimento de Salario")]
    [InlineData("\"Recebimento de Salario\"")]
    [InlineData("na conta Recebimento de Salario este ano")]
    public void ExtractSubject_keeps_salary_account_name(string fragment)
    {
        var subject = IanQuestionClassifier.ExtractSubject(
            fragment.StartsWith("na ", StringComparison.Ordinal)
                ? $"Quanto eu recebi {fragment}"
                : $"Quanto eu recebi na conta {fragment} este ano");

        Assert.Equal("Recebimento de Salario", subject);
    }
}
