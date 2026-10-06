using Conora.Services.Contracts;

namespace Conora.Services;

/// <summary>Static initial help content (spec v1.1 C15). Leomar replaces these texts later.</summary>
public sealed class HelpService
{
    private static readonly IReadOnlyList<HelpStepResponse> Steps =
    [
        new(1, "diagnosticar", "Diagnosticar",
            "Descubra para onde seu dinheiro vai hoje. Informe a renda bruta, o INSS e o IR para ver a renda gastável do mês."),
        new(2, "organizar", "Organizar",
            "Cadastre suas contas, cartões e categorias. Cada centavo precisa ter um lugar para ser registrado."),
        new(3, "planejar", "Planejar",
            "Defina quanto pode gastar em cada categoria no mês. O orçamento transforma intenção em limite."),
        new(4, "registrar", "Registrar",
            "Lance receitas e despesas no dia a dia. A movimentação é a única fonte da verdade do aplicativo."),
        new(5, "acompanhar", "Acompanhar",
            "Compare o planejado com o realizado. Os alertas avisam aos 70%, aos 100% e quando o limite é ultrapassado."),
        new(6, "corrigir", "Corrigir",
            "Ajuste o orçamento do mês aberto quando a realidade mudar e feche o mês para travar o histórico."),
        new(7, "construir", "Construir",
            "Direcione o que sobra para projetos de vida e acompanhe seu patrimônio e sua reserva.")
    ];

    private static readonly IReadOnlyList<HelpTermResponse> Glossary =
    [
        new("renda-gastavel", "Renda gastável",
            "É o valor que realmente chega para você usar no mês: renda bruta menos INSS e IR. O dízimo não é descontado da renda; é uma despesa em Contribuições/Doações."),
        new("essencial", "Essencial",
            "Gastos sem os quais a vida do mês não funciona, como moradia, alimentação, transporte, saúde e educação. A reserva é calculada sobre eles."),
        new("social", "Social",
            "Gastos de convivência e escolha, como lazer, presentes e saídas. São os primeiros a ajustar quando o mês aperta."),
        new("projeto-de-vida", "Projeto de vida",
            "Um objetivo com meta, prazo e valor acumulado, como uma viagem ou a entrada de um imóvel. Os aportes são acompanhados sem simulação de rentabilidade.")
    ];

    public HelpResponse Get() => new(Steps, Glossary);

    public HelpStepResponse? GetStep(string key)
        => Steps.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}
