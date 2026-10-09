using Conora.Domain.Enums;

namespace Conora.Domain.Catalog;

public sealed record SystemChartAccountDefinition(
    string Code,
    string Name,
    ChartAccountLevel Level,
    ChartSection Section,
    string? ParentCode,
    int SortOrder);

/// <summary>Seeded chart of accounts from Controle Orçamentário_familiar_APP.xlsx.</summary>
public static class SystemChartAccounts
{
    public const string Tithe = "DISC_TITHE";
    public const string Offerings = "DISC_OFFERINGS";

    public static readonly IReadOnlyList<SystemChartAccountDefinition> All = Build();

    private static IReadOnlyList<SystemChartAccountDefinition> Build()
    {
        var list = new List<SystemChartAccountDefinition>();
        var order = 0;

        void Root(string code, string name, ChartSection section)
            => list.Add(new(code, name, ChartAccountLevel.Root, section, null, order++));

        void Group(string code, string name, ChartSection section, string parent)
            => list.Add(new(code, name, ChartAccountLevel.Group, section, parent, order++));

        void Leaf(string code, string name, ChartSection section, string parent)
            => list.Add(new(code, name, ChartAccountLevel.Analytical, section, parent, order++));

        // 1. Receita
        Root("INC", "Receita", ChartSection.Income);
        Leaf("INC_SALARY", "Salário", ChartSection.Income, "INC");
        Leaf("INC_ROLE", "Função", ChartSection.Income, "INC");
        Leaf("INC_SENIORITY", "Adicional por tempo de serviço", ChartSection.Income, "INC");
        Leaf("INC_NIGHT", "Adicional noturno", ChartSection.Income, "INC");
        Leaf("INC_INCORP", "Incorporação gratificada", ChartSection.Income, "INC");
        Leaf("INC_OVERTIME", "Horas extras", ChartSection.Income, "INC");
        Leaf("INC_MEAL", "Ticket alimentação", ChartSection.Income, "INC");
        Leaf("INC_REMOTE", "Ajuda de custo trabalho remoto", ChartSection.Income, "INC");
        Leaf("INC_13TH", "13º salário", ChartSection.Income, "INC");
        Leaf("INC_PLR", "Participação nos Lucros", ChartSection.Income, "INC");
        Leaf("INC_VACATION", "Férias", ChartSection.Income, "INC");
        Leaf("INC_IR_REFUND", "Restituição de IR", ChartSection.Income, "INC");
        Leaf("INC_EXTRA", "Renda extra", ChartSection.Income, "INC");

        // 2. Desconto
        Root("DISC", "Desconto", ChartSection.Discount);
        Leaf("DISC_INSS", "Contribuição INSS", ChartSection.Discount, "DISC");
        Leaf("DISC_IR", "Imposto de Renda", ChartSection.Discount, "DISC");
        Leaf("DISC_UNION", "Contribuição Sindical", ChartSection.Discount, "DISC");
        Leaf(Tithe, "Dízimos", ChartSection.Discount, "DISC");
        Leaf(Offerings, "Ofertas", ChartSection.Discount, "DISC");

        // 3. Projetos de vida / Investimentos
        Root("LIFE", "Projetos de vida / Investimentos", ChartSection.LifeProject);
        Group("LIFE_SHORT", "Curto prazo", ChartSection.LifeProject, "LIFE");
        Leaf("LIFE_RESERVE", "Reserva de emergência", ChartSection.LifeProject, "LIFE_SHORT");
        Leaf("LIFE_US", "Viagem para EUA", ChartSection.LifeProject, "LIFE_SHORT");
        Group("LIFE_MID", "Médio prazo", ChartSection.LifeProject, "LIFE");
        Leaf("LIFE_REAL_ESTATE", "Consórcio Imobiliário", ChartSection.LifeProject, "LIFE_MID");
        Leaf("LIFE_CAR", "Consórcio automóvel", ChartSection.LifeProject, "LIFE_MID");
        Leaf("LIFE_COLLEGE", "Faculdade filhos", ChartSection.LifeProject, "LIFE_MID");
        Leaf("LIFE_EUROPE", "Viagem para Europa", ChartSection.LifeProject, "LIFE_MID");
        Group("LIFE_LONG", "Longo prazo", ChartSection.LifeProject, "LIFE");
        Leaf("LIFE_FUNCEF", "Aposentadoria - Previdência Privada - FUNCEF", ChartSection.LifeProject, "LIFE_LONG");
        Leaf("LIFE_STOCKS", "Aposentadoria - Ações", ChartSection.LifeProject, "LIFE_LONG");
        Leaf("LIFE_LIFE_INS", "Seguro de vida", ChartSection.LifeProject, "LIFE_LONG");

        // 4. Essencial
        Root("ESS", "Essencial", ChartSection.Essential);
        Group("ESS_HOUSING", "Habitação", ChartSection.Essential, "ESS");
        Leaf("ESS_RENT", "Aluguel", ChartSection.Essential, "ESS_HOUSING");
        Leaf("ESS_DEPOSIT", "Seguro fiança", ChartSection.Essential, "ESS_HOUSING");
        Leaf("ESS_CONDO", "Condomínio", ChartSection.Essential, "ESS_HOUSING");
        Leaf("ESS_IPTU", "IPTU e Taxa de lixo", ChartSection.Essential, "ESS_HOUSING");
        Leaf("ESS_CLEANER", "Diarista", ChartSection.Essential, "ESS_HOUSING");
        Leaf("ESS_POWER", "Energia", ChartSection.Essential, "ESS_HOUSING");
        Leaf("ESS_WATER", "Água", ChartSection.Essential, "ESS_HOUSING");
        Leaf("ESS_PHONE", "Telefone Fixo / Celular", ChartSection.Essential, "ESS_HOUSING");
        Leaf("ESS_GAS", "Gás", ChartSection.Essential, "ESS_HOUSING");
        Leaf("ESS_INTERNET", "Internet / TV", ChartSection.Essential, "ESS_HOUSING");
        Leaf("ESS_FURNITURE", "Móveis e utensílios", ChartSection.Essential, "ESS_HOUSING");
        Leaf("ESS_MAINT", "Manutenção e consertos", ChartSection.Essential, "ESS_HOUSING");
        Group("ESS_FOOD", "Alimentação", ChartSection.Essential, "ESS");
        Leaf("ESS_MARKET", "Supermercado", ChartSection.Essential, "ESS_FOOD");
        Leaf("ESS_FAIR", "Feira", ChartSection.Essential, "ESS_FOOD");
        Leaf("ESS_BAKERY", "Padaria", ChartSection.Essential, "ESS_FOOD");
        Group("ESS_CLOTHES", "Vestuário", ChartSection.Essential, "ESS");
        Leaf("ESS_CLOTHES_ITEM", "Roupas", ChartSection.Essential, "ESS_CLOTHES");
        Leaf("ESS_SHOES", "Calçados", ChartSection.Essential, "ESS_CLOTHES");
        Group("ESS_HEALTH", "Saúde", ChartSection.Essential, "ESS");
        Leaf("ESS_HEALTH_PLAN", "Plano de Saúde - Mensalidade", ChartSection.Essential, "ESS_HEALTH");
        Leaf("ESS_HEALTH_COPAY", "Plano de Saúde - Participação", ChartSection.Essential, "ESS_HEALTH");
        Leaf("ESS_MEDS", "Medicamentos", ChartSection.Essential, "ESS_HEALTH");
        Leaf("ESS_DENTIST", "Dentista", ChartSection.Essential, "ESS_HEALTH");
        Group("ESS_EDU", "Educação / Profissão", ChartSection.Essential, "ESS");
        Leaf("ESS_SCHOOL", "Escola", ChartSection.Essential, "ESS_EDU");
        Leaf("ESS_MBA", "MBA", ChartSection.Essential, "ESS_EDU");
        Leaf("ESS_ENGLISH", "Inglês", ChartSection.Essential, "ESS_EDU");
        Leaf("ESS_INCENTIVE", "Devolução incentivo", ChartSection.Essential, "ESS_EDU");
        Leaf("ESS_MATERIAL", "Material escolar", ChartSection.Essential, "ESS_EDU");
        Leaf("ESS_UNIFORM", "Uniforme", ChartSection.Essential, "ESS_EDU");
        Leaf("ESS_COURSES", "Cursos extras", ChartSection.Essential, "ESS_EDU");
        Leaf("ESS_BOOKS", "Livros", ChartSection.Essential, "ESS_EDU");
        Group("ESS_TRANSPORT", "Transporte", ChartSection.Essential, "ESS");
        Leaf("ESS_IPVA", "IPVA", ChartSection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_CAR_MAINT", "Carro / Bicicletas (manutenção)", ChartSection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_CAR_INS", "Seguros do carro/bicicleta", ChartSection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_FUEL", "Combustível", ChartSection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_PARK", "Estacionamento", ChartSection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_WASH", "Lavagem do carro", ChartSection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_PASS", "Vale Transporte", ChartSection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_UBER", "Uber", ChartSection.Essential, "ESS_TRANSPORT");
        Group("ESS_TAX", "Impostos", ChartSection.Essential, "ESS");
        Leaf("ESS_IRRF", "IRRF", ChartSection.Essential, "ESS_TAX");
        Group("ESS_DEBT", "Dívidas / Despesas financeiras", ChartSection.Essential, "ESS");
        Leaf("ESS_DEBT_1", "Dívida 1", ChartSection.Essential, "ESS_DEBT");
        Leaf("ESS_DEBT_2", "Dívida 2", ChartSection.Essential, "ESS_DEBT");
        Leaf("ESS_DEBT_3", "Dívida 3", ChartSection.Essential, "ESS_DEBT");
        Leaf("ESS_VAC_ADV", "Devolução de adiantamento de férias", ChartSection.Essential, "ESS_DEBT");
        Leaf("ESS_13TH_ADV", "Devolução de 13º Salário", ChartSection.Essential, "ESS_DEBT");
        Leaf("ESS_INTEREST", "Juros", ChartSection.Essential, "ESS_DEBT");
        Leaf("ESS_FINES", "Multas", ChartSection.Essential, "ESS_DEBT");
        Leaf("ESS_BANK_FEE", "Tarifas bancárias - manutenção de conta", ChartSection.Essential, "ESS_DEBT");
        Group("ESS_CARE", "Cuidados pessoais", ChartSection.Essential, "ESS");
        Leaf("ESS_HAIR", "Cabeleireiro", ChartSection.Essential, "ESS_CARE");
        Leaf("ESS_WAX", "Salão de beleza - Depilação", ChartSection.Essential, "ESS_CARE");
        Leaf("ESS_AESTHETIC", "Salão de beleza - Procedimento estético", ChartSection.Essential, "ESS_CARE");
        Leaf("ESS_NAIL", "Salão de beleza - Unha", ChartSection.Essential, "ESS_CARE");
        Leaf("ESS_SALON_HAIR", "Salão de beleza - Cabelo", ChartSection.Essential, "ESS_CARE");
        Leaf("ESS_GYM", "Academia - Musculação", ChartSection.Essential, "ESS_CARE");
        Leaf("ESS_MARTIAL", "Academia - Arte Marcial", ChartSection.Essential, "ESS_CARE");
        Leaf("ESS_VOLLEY", "Esporte - Voleibol", ChartSection.Essential, "ESS_CARE");
        Leaf("ESS_FOOTBALL", "Esporte - Futebol", ChartSection.Essential, "ESS_CARE");

        // 5. Social
        Root("SOC", "Social", ChartSection.Social);
        Group("SOC_GIFTS", "Presentes", ChartSection.Social, "SOC");
        Leaf("SOC_FRIENDS", "Amigos", ChartSection.Social, "SOC_GIFTS");
        Leaf("SOC_FAMILY", "Parentes", ChartSection.Social, "SOC_GIFTS");
        Leaf("SOC_COLLEAGUES", "Colegas de trabalho / Igreja", ChartSection.Social, "SOC_GIFTS");
        Group("SOC_REWARD", "Recompensas familiar", ChartSection.Social, "SOC");
        Leaf("SOC_TRAVEL", "Viagens extras (congressos / Família)", ChartSection.Social, "SOC_REWARD");
        Leaf("SOC_CLUB", "Mensalidade - Clubes", ChartSection.Social, "SOC_REWARD");
        Leaf("SOC_ALLOWANCE", "Mesadas", ChartSection.Social, "SOC_REWARD");
        Leaf("SOC_RESTAURANT", "Restaurante", ChartSection.Social, "SOC_REWARD");
        Leaf("SOC_SNACKS", "Lanches", ChartSection.Social, "SOC_REWARD");
        Leaf("SOC_LEISURE", "Lazer", ChartSection.Social, "SOC_REWARD");
        Group("SOC_SUB", "Assinaturas", ChartSection.Social, "SOC");
        Leaf("SOC_GAME", "Game", ChartSection.Social, "SOC_SUB");
        Leaf("SOC_NEWS", "Jornais e TV", ChartSection.Social, "SOC_SUB");
        Leaf("SOC_CLOUD", "Internet - Armazenamento", ChartSection.Social, "SOC_SUB");
        Leaf("SOC_STREAM", "Netflix/Prime/Disney", ChartSection.Social, "SOC_SUB");
        Leaf("SOC_AI", "Inteligência Artificial", ChartSection.Social, "SOC_SUB");

        // 6. Ativo
        Root("ASSET", "Ativo", ChartSection.Asset);
        Group("ASSET_USE", "Bens de Uso", ChartSection.Asset, "ASSET");
        Leaf("ASSET_CAR", "Automóvel", ChartSection.Asset, "ASSET_USE");
        Group("ASSET_NONUSE", "Bens de Não Uso", ChartSection.Asset, "ASSET");
        Leaf("ASSET_STOCKS", "Ações longo prazo", ChartSection.Asset, "ASSET_NONUSE");

        // 7. Passivo
        Root("LIAB", "Passivo", ChartSection.Liability);
        Leaf("LIAB_OVER_BANESTES", "Cheque especial Banestes", ChartSection.Liability, "LIAB");
        Leaf("LIAB_OVER_CEF", "Cheque especial CEF", ChartSection.Liability, "LIAB");
        Leaf("LIAB_CONS_BANESTES", "Consignado Banestes", ChartSection.Liability, "LIAB");
        Leaf("LIAB_CONS_CEF", "Consignado CEF", ChartSection.Liability, "LIAB");
        Leaf("LIAB_CARD", "Cartão de crédito", ChartSection.Liability, "LIAB");

        return list;
    }

    public static string SectionLabel(ChartSection section) => section switch
    {
        ChartSection.Income => "Receita",
        ChartSection.Discount => "Desconto",
        ChartSection.LifeProject => "Projetos de vida / Investimentos",
        ChartSection.Essential => "Essencial",
        ChartSection.Social => "Social",
        ChartSection.Asset => "Ativo",
        ChartSection.Liability => "Passivo",
        _ => section.ToString()
    };

    public static string SectionHint(ChartSection section) => section switch
    {
        ChartSection.Income => "o que entra",
        ChartSection.Discount => "o que reduz a renda",
        ChartSection.LifeProject => "o que você separa para o futuro",
        ChartSection.Essential => "o que a casa precisa",
        ChartSection.Social => "o que é escolha",
        ChartSection.Asset => "o que você tem",
        ChartSection.Liability => "o que você deve",
        _ => ""
    };

    public static bool IsCashFlowSection(ChartSection section)
        => section is ChartSection.Income or ChartSection.Discount or ChartSection.LifeProject
            or ChartSection.Essential or ChartSection.Social;

    public static bool IsPatrimonySection(ChartSection section)
        => section is ChartSection.Asset or ChartSection.Liability;
}
