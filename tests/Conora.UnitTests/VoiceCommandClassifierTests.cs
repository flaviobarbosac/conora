using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class VoiceCommandClassifierTests
{
    [Fact]
    public void OpenBudget_navigates()
    {
        var result = VoiceCommandClassifier.Classify("abre a tela de orçamento");
        Assert.Equal(VoiceCommandClassifier.Navigate, result.Intent);
        Assert.Equal("/orcamento", result.Route);
    }

    [Fact]
    public void OpenRaioX_navigates()
    {
        var result = VoiceCommandClassifier.Classify("abre o raio-x");
        Assert.Equal("/raio-x", result.Route);
    }

    [Fact]
    public void PostExpense_isAnActionAndDoesNotSave()
    {
        var result = VoiceCommandClassifier.Classify("tô lançando aqui uma conta de água no valor de 200 reais");
        Assert.Equal(VoiceCommandClassifier.Action, result.Intent);
        Assert.Equal(VoiceCommandClassifier.CreateEntry, result.Action);
        Assert.Equal(200m, result.Entry?.Amount);
        Assert.Contains("água", result.Entry?.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Expense", result.Entry?.Type);
    }

    [Fact]
    public void FullSentence_fillsEveryField()
    {
        var result = VoiceCommandClassifier.Classify(
            "vamos lançar uma despesa de quinhentos reais, referente a INSS, ela é a pagar, foi realizada hoje, foi paga pela conta do Banco do Brasil, uma conta pessoal");
        Assert.Equal(VoiceCommandClassifier.Action, result.Intent);
        Assert.Equal("Expense", result.Entry?.Type);
        Assert.Equal(500m, result.Entry?.Amount);
        Assert.Equal("hoje", result.Entry?.Date);
        Assert.Contains("INSS", result.Entry?.Description);
        Assert.Contains("Banco do Brasil", result.Entry?.AccountName);
        Assert.Equal("Pessoal", result.Entry?.CategoryName);
    }

    [Fact]
    public void AmountOnly_startsEntryWithoutInventingTheRest()
    {
        var result = VoiceCommandClassifier.Classify("quero fazer um lançamento de 500 reais");
        Assert.Equal(VoiceCommandClassifier.Action, result.Intent);
        Assert.Equal(500m, result.Entry?.Amount);
        Assert.Equal("", result.Entry?.Type);
        Assert.Null(result.Entry?.Date);
        Assert.Null(result.Entry?.AccountName);
    }

    [Theory]
    [InlineData("mover pra tela de orçamento", "/orcamento")]
    [InlineData("ir pra tela de lançamentos", "/lancamentos")]
    [InlineData("ir pra orçamento", "/orcamento")]
    [InlineData("vai pra tela do raio-x", "/raio-x")]
    public void MoveToScreen_navigates(string transcript, string route)
    {
        var result = VoiceCommandClassifier.Classify(transcript);
        Assert.Equal(VoiceCommandClassifier.Navigate, result.Intent);
        Assert.Equal(route, result.Route);
    }

    [Fact]
    public void BudgetRegister_isABudget_notAScreen()
    {
        var result = VoiceCommandClassifier.Classify("registra o orçamento da categoria mercado no valor de 200 reais");
        Assert.Equal(VoiceCommandClassifier.CreateBudget, result.Action);
        Assert.Equal(200m, result.Entry?.Amount);
        Assert.Contains("mercado", result.Entry?.CategoryName);
    }

    [Fact]
    public void BudgetQuestion_isAReport_notAScreen()
    {
        var result = VoiceCommandClassifier.Classify("como está o orçamento?");
        Assert.Equal(VoiceCommandClassifier.Ask, result.Intent);
        Assert.Null(result.Route);
    }

    [Fact]
    public void MonthQuestion_isAReport()
    {
        var result = VoiceCommandClassifier.Classify("como está meu mês?");
        Assert.Equal(VoiceCommandClassifier.Ask, result.Intent);
        Assert.Null(result.Action);
    }

    [Fact]
    public void Aporte_isAProjectContribution()
    {
        var result = VoiceCommandClassifier.Classify("lança um aporte de 200 reais no projeto viagem");
        Assert.Equal(VoiceCommandClassifier.Action, result.Intent);
        Assert.Equal(VoiceCommandClassifier.CreateEntry, result.Action);
        Assert.Equal("ProjectContribution", result.Entry?.Type);
        Assert.Equal(200m, result.Entry?.Amount);
    }

    [Fact]
    public void HelpQuestion_listsWhatIanCanDo()
    {
        var result = VoiceCommandClassifier.Classify("em que você pode me ajudar?");
        Assert.Equal(VoiceCommandClassifier.Ask, result.Intent);
        Assert.Equal(VoiceCommandClassifier.HelpReply, result.Reply);
        Assert.Contains("despesa", result.Reply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("receita", result.Reply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orçamento", result.Reply, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Route);
        Assert.Null(result.Action);
    }

    [Fact]
    public void ClockQuestion_staysAnAsk()
    {
        var result = VoiceCommandClassifier.Classify("que horas são agora?");
        Assert.Equal(VoiceCommandClassifier.Ask, result.Intent);
        Assert.Null(result.Route);
        Assert.Null(result.Action);
    }
}
