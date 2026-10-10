using Conora.Domain.Enums;

namespace Conora.Domain.Catalog;

public sealed record SystemCategoryDefinition(
    string Code,
    string Name,
    CategoryLevel Level,
    CategorySection Section,
    string? ParentCode,
    int SortOrder);

/// <summary>Seeded categories: Orçamento / Patrimônio masters with nested groups.</summary>
public static class SystemCategories
{
    public const string Tithe = "DISC_TITHE";
    public const string Offerings = "DISC_OFFERINGS";

    public static readonly IReadOnlyList<SystemCategoryDefinition> All = Build();

    private static IReadOnlyList<SystemCategoryDefinition> Build()
    {
        var list = new List<SystemCategoryDefinition>();
        var order = 0;

        void Root(string code, string name, CategorySection section)
            => list.Add(new(code, name, CategoryLevel.Root, section, null, order++));

        void Group(string code, string name, CategorySection section, string parent)
            => list.Add(new(code, name, CategoryLevel.Group, section, parent, order++));

        void Leaf(string code, string name, CategorySection section, string parent)
            => list.Add(new(code, name, CategoryLevel.Analytical, section, parent, order++));

        // Masters
        Root("BUDGET", "Orçamento", CategorySection.Budget);
        Root("PATRIMONY", "Patrimônio", CategorySection.Patrimony);

        // Orçamento → Receita
        Group("INC", "Receita", CategorySection.Income, "BUDGET");
        Leaf("INC_SALARY", "Salário", CategorySection.Income, "INC");
        Leaf("INC_ROLE", "Função", CategorySection.Income, "INC");
        Leaf("INC_SENIORITY", "Adicional por tempo de serviço", CategorySection.Income, "INC");
        Leaf("INC_NIGHT", "Adicional noturno", CategorySection.Income, "INC");
        Leaf("INC_INCORP", "Incorporação gratificada", CategorySection.Income, "INC");
        Leaf("INC_OVERTIME", "Horas extras", CategorySection.Income, "INC");
        Leaf("INC_MEAL", "Ticket alimentação", CategorySection.Income, "INC");
        Leaf("INC_REMOTE", "Ajuda de custo trabalho remoto", CategorySection.Income, "INC");
        Leaf("INC_13TH", "13º salário", CategorySection.Income, "INC");
        Leaf("INC_PLR", "Participação nos Lucros", CategorySection.Income, "INC");
        Leaf("INC_VACATION", "Férias", CategorySection.Income, "INC");
        Leaf("INC_IR_REFUND", "Restituição de IR", CategorySection.Income, "INC");
        Leaf("INC_EXTRA", "Renda extra", CategorySection.Income, "INC");

        // Orçamento → Despesa
        Group("EXPENSE", "Despesa", CategorySection.Expense, "BUDGET");

        Group("DISC", "Descontos", CategorySection.Discount, "EXPENSE");
        Leaf("DISC_INSS", "Contribuição INSS", CategorySection.Discount, "DISC");
        Leaf("DISC_IR", "Imposto de Renda", CategorySection.Discount, "DISC");
        Leaf("DISC_UNION", "Contribuição Sindical", CategorySection.Discount, "DISC");
        Leaf(Tithe, "Dízimos", CategorySection.Discount, "DISC");
        Leaf(Offerings, "Ofertas", CategorySection.Discount, "DISC");

        Group("LIFE", "Projeto de vida", CategorySection.LifeProject, "EXPENSE");
        Group("LIFE_SHORT", "Curto prazo", CategorySection.LifeProject, "LIFE");
        Leaf("LIFE_RESERVE", "Reserva de emergência", CategorySection.LifeProject, "LIFE_SHORT");
        Leaf("LIFE_US", "Viagem para EUA", CategorySection.LifeProject, "LIFE_SHORT");
        Group("LIFE_MID", "Médio prazo", CategorySection.LifeProject, "LIFE");
        Leaf("LIFE_REAL_ESTATE", "Consórcio Imobiliário", CategorySection.LifeProject, "LIFE_MID");
        Leaf("LIFE_CAR", "Consórcio automóvel", CategorySection.LifeProject, "LIFE_MID");
        Leaf("LIFE_COLLEGE", "Faculdade filhos", CategorySection.LifeProject, "LIFE_MID");
        Leaf("LIFE_EUROPE", "Viagem para Europa", CategorySection.LifeProject, "LIFE_MID");
        Group("LIFE_LONG", "Longo prazo", CategorySection.LifeProject, "LIFE");
        Leaf("LIFE_FUNCEF", "Aposentadoria - Previdência Privada - FUNCEF", CategorySection.LifeProject, "LIFE_LONG");
        Leaf("LIFE_STOCKS", "Aposentadoria - Ações", CategorySection.LifeProject, "LIFE_LONG");
        Leaf("LIFE_LIFE_INS", "Seguro de vida", CategorySection.LifeProject, "LIFE_LONG");

        Group("ESS", "Essencial", CategorySection.Essential, "EXPENSE");
        Group("ESS_HOUSING", "Habitação", CategorySection.Essential, "ESS");
        Leaf("ESS_RENT", "Aluguel", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_DEPOSIT", "Seguro fiança", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_CONDO", "Condomínio", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_IPTU", "IPTU e Taxa de lixo", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_CLEANER", "Diarista", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_POWER", "Energia", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_WATER", "Água", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_PHONE", "Telefone Fixo / Celular", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_GAS", "Gás", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_INTERNET", "Internet / TV", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_FURNITURE", "Móveis e utensílios", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_MAINT", "Manutenção e consertos", CategorySection.Essential, "ESS_HOUSING");
        Group("ESS_FOOD", "Alimentação", CategorySection.Essential, "ESS");
        Leaf("ESS_MARKET", "Supermercado", CategorySection.Essential, "ESS_FOOD");
        Leaf("ESS_FAIR", "Feira", CategorySection.Essential, "ESS_FOOD");
        Leaf("ESS_BAKERY", "Padaria", CategorySection.Essential, "ESS_FOOD");
        Group("ESS_CLOTHES", "Vestuário", CategorySection.Essential, "ESS");
        Leaf("ESS_CLOTHES_ITEM", "Roupas", CategorySection.Essential, "ESS_CLOTHES");
        Leaf("ESS_SHOES", "Calçados", CategorySection.Essential, "ESS_CLOTHES");
        Group("ESS_HEALTH", "Saúde", CategorySection.Essential, "ESS");
        Leaf("ESS_HEALTH_PLAN", "Plano de Saúde - Mensalidade", CategorySection.Essential, "ESS_HEALTH");
        Leaf("ESS_HEALTH_COPAY", "Plano de Saúde - Participação", CategorySection.Essential, "ESS_HEALTH");
        Leaf("ESS_MEDS", "Medicamentos", CategorySection.Essential, "ESS_HEALTH");
        Leaf("ESS_DENTIST", "Dentista", CategorySection.Essential, "ESS_HEALTH");
        Group("ESS_EDU", "Educação / Profissão", CategorySection.Essential, "ESS");
        Leaf("ESS_SCHOOL", "Escola", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_MBA", "MBA", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_ENGLISH", "Inglês", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_INCENTIVE", "Devolução incentivo", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_MATERIAL", "Material escolar", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_UNIFORM", "Uniforme", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_COURSES", "Cursos extras", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_BOOKS", "Livros", CategorySection.Essential, "ESS_EDU");
        Group("ESS_TRANSPORT", "Transporte", CategorySection.Essential, "ESS");
        Leaf("ESS_IPVA", "IPVA", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_CAR_MAINT", "Carro / Bicicletas (manutenção)", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_CAR_INS", "Seguros do carro/bicicleta", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_FUEL", "Combustível", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_PARK", "Estacionamento", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_WASH", "Lavagem do carro", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_PASS", "Vale Transporte", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_UBER", "Uber", CategorySection.Essential, "ESS_TRANSPORT");
        Group("ESS_TAX", "Impostos", CategorySection.Essential, "ESS");
        Leaf("ESS_IRRF", "IRRF", CategorySection.Essential, "ESS_TAX");
        Group("ESS_DEBT", "Dívidas / Despesas financeiras", CategorySection.Essential, "ESS");
        Leaf("ESS_DEBT_1", "Dívida 1", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_DEBT_2", "Dívida 2", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_DEBT_3", "Dívida 3", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_VAC_ADV", "Devolução de adiantamento de férias", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_13TH_ADV", "Devolução de 13º Salário", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_INTEREST", "Juros", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_FINES", "Multas", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_BANK_FEE", "Tarifas bancárias - manutenção de conta", CategorySection.Essential, "ESS_DEBT");
        Group("ESS_CARE", "Cuidados pessoais", CategorySection.Essential, "ESS");
        Leaf("ESS_HAIR", "Cabeleireiro", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_WAX", "Salão de beleza - Depilação", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_AESTHETIC", "Salão de beleza - Procedimento estético", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_NAIL", "Salão de beleza - Unha", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_SALON_HAIR", "Salão de beleza - Cabelo", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_GYM", "Academia - Musculação", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_MARTIAL", "Academia - Arte Marcial", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_VOLLEY", "Esporte - Voleibol", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_FOOTBALL", "Esporte - Futebol", CategorySection.Essential, "ESS_CARE");

        Group("SOC", "Social", CategorySection.Social, "EXPENSE");
        Group("SOC_GIFTS", "Presentes", CategorySection.Social, "SOC");
        Leaf("SOC_FRIENDS", "Amigos", CategorySection.Social, "SOC_GIFTS");
        Leaf("SOC_FAMILY", "Parentes", CategorySection.Social, "SOC_GIFTS");
        Leaf("SOC_COLLEAGUES", "Colegas de trabalho / Igreja", CategorySection.Social, "SOC_GIFTS");
        Group("SOC_REWARD", "Recompensas familiar", CategorySection.Social, "SOC");
        Leaf("SOC_TRAVEL", "Viagens extras (congressos / Família)", CategorySection.Social, "SOC_REWARD");
        Leaf("SOC_CLUB", "Mensalidade - Clubes", CategorySection.Social, "SOC_REWARD");
        Leaf("SOC_ALLOWANCE", "Mesadas", CategorySection.Social, "SOC_REWARD");
        Leaf("SOC_RESTAURANT", "Restaurante", CategorySection.Social, "SOC_REWARD");
        Leaf("SOC_SNACKS", "Lanches", CategorySection.Social, "SOC_REWARD");
        Leaf("SOC_LEISURE", "Lazer", CategorySection.Social, "SOC_REWARD");
        Group("SOC_SUB", "Assinaturas", CategorySection.Social, "SOC");
        Leaf("SOC_GAME", "Game", CategorySection.Social, "SOC_SUB");
        Leaf("SOC_NEWS", "Jornais e TV", CategorySection.Social, "SOC_SUB");
        Leaf("SOC_CLOUD", "Internet - Armazenamento", CategorySection.Social, "SOC_SUB");
        Leaf("SOC_STREAM", "Netflix/Prime/Disney", CategorySection.Social, "SOC_SUB");
        Leaf("SOC_AI", "Inteligência Artificial", CategorySection.Social, "SOC_SUB");

        // Patrimônio → Ativo / Passivo
        Group("ASSET", "Ativo", CategorySection.Asset, "PATRIMONY");
        Group("ASSET_USE", "Bens de Uso", CategorySection.Asset, "ASSET");
        Leaf("ASSET_CAR", "Automóvel", CategorySection.Asset, "ASSET_USE");
        Group("ASSET_NONUSE", "Bens de Não Uso", CategorySection.Asset, "ASSET");
        Leaf("ASSET_STOCKS", "Ações longo prazo", CategorySection.Asset, "ASSET_NONUSE");

        Group("LIAB", "Passivo", CategorySection.Liability, "PATRIMONY");
        Leaf("LIAB_OVER_BANESTES", "Cheque especial Banestes", CategorySection.Liability, "LIAB");
        Leaf("LIAB_OVER_CEF", "Cheque especial CEF", CategorySection.Liability, "LIAB");
        Leaf("LIAB_CONS_BANESTES", "Consignado Banestes", CategorySection.Liability, "LIAB");
        Leaf("LIAB_CONS_CEF", "Consignado CEF", CategorySection.Liability, "LIAB");
        Leaf("LIAB_CARD", "Cartão de crédito", CategorySection.Liability, "LIAB");

        return list;
    }

    public static string SectionLabel(CategorySection section) => section switch
    {
        CategorySection.Budget => "Orçamento",
        CategorySection.Expense => "Despesa",
        CategorySection.Patrimony => "Patrimônio",
        CategorySection.Income => "Receita",
        CategorySection.Discount => "Descontos",
        CategorySection.LifeProject => "Projeto de vida",
        CategorySection.Essential => "Essencial",
        CategorySection.Social => "Social",
        CategorySection.Asset => "Ativo",
        CategorySection.Liability => "Passivo",
        _ => section.ToString()
    };

    public static string SectionHint(CategorySection section) => section switch
    {
        CategorySection.Budget => "o que entra e sai no mês",
        CategorySection.Expense => "saídas do mês",
        CategorySection.Patrimony => "o que você tem e o que deve",
        CategorySection.Income => "o que entra",
        CategorySection.Discount => "o que reduz a renda",
        CategorySection.LifeProject => "o que você separa para o futuro",
        CategorySection.Essential => "o que a casa precisa",
        CategorySection.Social => "o que é escolha",
        CategorySection.Asset => "o que você tem",
        CategorySection.Liability => "o que você deve",
        _ => ""
    };

    public static bool IsCashFlowSection(CategorySection section)
        => section is CategorySection.Income or CategorySection.Discount or CategorySection.LifeProject
            or CategorySection.Essential or CategorySection.Social;

    public static bool IsPatrimonySection(CategorySection section)
        => section is CategorySection.Asset or CategorySection.Liability;

    public static bool IsStructuralSection(CategorySection section)
        => section is CategorySection.Budget or CategorySection.Expense or CategorySection.Patrimony;

    /// <summary>Parents that may receive a new analytical account (posting sections only).</summary>
    public static bool AcceptsAnalyticalChild(CategorySection section)
        => IsCashFlowSection(section) || IsPatrimonySection(section);
}
