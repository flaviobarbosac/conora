using System.Globalization;
using Conora.Services;
using Conora.Services.Contracts;
using Xunit;

namespace Conora.UnitTests;

public class WhatsAppInboundTests
{
    [Fact]
    public void Extract_detects_audio_media_from_cloud_api_payload()
    {
        const string body = """
            {"entry":[{"changes":[{"value":{"messages":[{"from":"5527999999999","type":"audio","audio":{"id":"media-audio-1"}}]}}]}]}
            """;

        var messages = WhatsAppInbound.Extract(body);
        Assert.Single(messages);
        Assert.Equal(WhatsAppInbound.Kind.Audio, messages[0].Kind);
        Assert.Equal("media-audio-1", messages[0].MediaId);
        Assert.Equal("5527999999999", messages[0].From);
    }

    [Fact]
    public void Extract_detects_text_and_image()
    {
        const string body = """
            {"entry":[{"changes":[{"value":{"messages":[
              {"from":"5527111","type":"text","text":{"body":"saldo"}},
              {"from":"5527111","type":"image","image":{"id":"img-1"}}
            ]}}]}]}
            """;

        var messages = WhatsAppInbound.Extract(body);
        Assert.Equal(2, messages.Count);
        Assert.Equal(WhatsAppInbound.Kind.Text, messages[0].Kind);
        Assert.Equal("saldo", messages[0].Text);
        Assert.Equal(WhatsAppInbound.Kind.Image, messages[1].Kind);
    }

    [Theory]
    [InlineData("Conor, o que você pode fazer pra mim?", WhatsAppInbound.QueryKind.Help)]
    [InlineData("ajuda", WhatsAppInbound.QueryKind.Help)]
    [InlineData("qual meu saldo", WhatsAppInbound.QueryKind.Balance)]
    [InlineData("extrato do mês", WhatsAppInbound.QueryKind.Statement)]
    [InlineData("dívidas em aberto", WhatsAppInbound.QueryKind.Debts)]
    [InlineData("quanto sobrou do orçamento", WhatsAppInbound.QueryKind.Budget)]
    [InlineData("quanto posso gastar de iFood ainda?", WhatsAppInbound.QueryKind.Budget)]
    [InlineData("como tá meu orçamento do mês?", WhatsAppInbound.QueryKind.Budget)]
    [InlineData("quanto posso gastar?", WhatsAppInbound.QueryKind.Budget)]
    [InlineData("resumo", WhatsAppInbound.QueryKind.Budget)]
    [InlineData("gastei 45 no mercado", WhatsAppInbound.QueryKind.None)]
    public void Classify_routes_help_and_queries_without_digits(string text, WhatsAppInbound.QueryKind expected)
    {
        Assert.Equal(expected, WhatsAppInbound.Classify(text));
    }

    [Theory]
    [InlineData("quanto posso gastar de iFood ainda?", "iFood")]
    [InlineData("quanto sobrou pra categoria alimentação", "alimentação")]
    [InlineData("limite da Moradia", "Moradia")]
    [InlineData("quanto sobrou do orçamento", null)]
    [InlineData("resumo", null)]
    public void ExtractBudgetCategoryHint_pulls_category_or_null(string text, string? expected)
    {
        Assert.Equal(expected, WhatsAppInbound.ExtractBudgetCategoryHint(text));
    }

    [Fact]
    public void FormatCategoryBudgetReply_shows_remaining_spend()
    {
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        var reply = WhatsAppInbound.FormatCategoryBudgetReply("Novembro/2026", "iFood", 300m, 120m, culture);
        Assert.Contains("iFood", reply);
        Assert.Contains("300,00", reply);
        Assert.Contains("120,00", reply);
        Assert.Contains("180,00", reply);
        Assert.Contains("ainda pode gastar", reply);
    }

    [Fact]
    public void FormatCategoryBudgetReply_shows_overspend()
    {
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        var reply = WhatsAppInbound.FormatCategoryBudgetReply("Novembro/2026", "iFood", 100m, 150m, culture);
        Assert.Contains("passou R$ 50,00", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatMonthBudgetReply_covers_global_month_snapshot()
    {
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        var reply = WhatsAppInbound.FormatMonthBudgetReply(
            "Novembro/2026",
            totalPlanned: 10_000m,
            totalActual: 8_250m,
            netIncome: 12_000m,
            openDebtsTotal: 3_200.40m,
            openDebtCount: 2,
            culture);

        Assert.Contains("10.000", reply);
        Assert.Contains("8.250", reply);
        Assert.Contains("1.750", reply);
        Assert.Contains("Receita líquida do mês: R$ 12.000", reply);
        Assert.Contains("3.200", reply);
        Assert.Contains("2 contas/dívidas", reply);
        Assert.Contains("Líquido disponível pra gastar: R$ 8.800", reply);
        Assert.DoesNotContain("iFood", reply);
    }

    [Fact]
    public void FormatMonthBudgetReply_handles_no_budget_and_no_debts()
    {
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        var reply = WhatsAppInbound.FormatMonthBudgetReply(
            "Novembro/2026",
            totalPlanned: 0m,
            totalActual: 0m,
            netIncome: 5_000m,
            openDebtsTotal: 0m,
            openDebtCount: 0,
            culture);

        Assert.Contains("ainda não tem orçamento planejado", reply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nada pendente", reply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Líquido disponível pra gastar: R$ 5.000", reply);
    }

    [Fact]
    public void ExtractBudgetCategoryHint_keeps_global_asks_without_category()
    {
        Assert.Null(WhatsAppInbound.ExtractBudgetCategoryHint("como tá meu orçamento do mês?"));
        Assert.Null(WhatsAppInbound.ExtractBudgetCategoryHint("quanto posso gastar?"));
        Assert.Equal("iFood", WhatsAppInbound.ExtractBudgetCategoryHint("quanto posso gastar de iFood ainda?"));
    }

    [Fact]
    public void Category_confirm_uses_condominium_tone()
    {
        var prompt = WhatsAppInbound.CategoryConfirmPrompt("paguei condomínio", "Moradia");
        Assert.Contains("Referente a", prompt);
        Assert.Contains("condomínio", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Moradia", prompt);
        Assert.Contains("Você confirma?", prompt);
    }

    [Fact]
    public void Session_steps_cover_guided_confirmation_flow()
    {
        Assert.Equal("ConfirmAmount", WhatsAppSessionSteps.ConfirmAmount);
        Assert.Equal("ConfirmCategory", WhatsAppSessionSteps.ConfirmCategory);
        Assert.Equal("ConfirmAccount", WhatsAppSessionSteps.ConfirmAccount);
        Assert.Equal("NeedDebtChoice", WhatsAppSessionSteps.NeedDebtChoice);
        Assert.Equal("ConfirmLaunch", WhatsAppSessionSteps.ConfirmLaunch);
    }
}
