using Conora.Domain.Enums;

namespace Conora.Domain.Catalog;

public sealed record SystemCategoryDefinition(
    string Code,
    string Name,
    CategoryKind Kind,
    bool IsEssential,
    BudgetBlock? Block = null,
    string? GroupName = null);

/// <summary>System categories seeded for every tenant. "Descontos sobre renda" is intentionally not launchable (spec v1.1).</summary>
public static class SystemCategories
{
    public const string Contributions = "CONTRIBUTIONS";
    public const string Transfer = "TRANSFER";

    public static readonly IReadOnlyList<SystemCategoryDefinition> All =
    [
        // Legacy coarse groups (kept for existing budget lines)
        new("HOUSING", "Habitação", CategoryKind.Expense, true, BudgetBlock.Essential, "Habitação"),
        new("FOOD", "Alimentação", CategoryKind.Expense, true, BudgetBlock.Social, "Alimentação"),
        new("TRANSPORT", "Transporte", CategoryKind.Expense, true, BudgetBlock.Essential, "Transporte"),
        new("HEALTH", "Saúde", CategoryKind.Expense, true, BudgetBlock.Essential, "Saúde"),
        new("EDUCATION", "Educação", CategoryKind.Expense, true, BudgetBlock.Essential, "Educação"),
        new("DEBTS", "Dívidas e financiamentos", CategoryKind.Expense, true, BudgetBlock.Essential, "Dívidas"),
        new("LEISURE", "Lazer", CategoryKind.Expense, false, BudgetBlock.Social, "Lazer"),
        new("CLOTHING", "Vestuário", CategoryKind.Expense, false, BudgetBlock.Social, "Vestuário"),
        new(Contributions, "Contribuições/Doações", CategoryKind.Expense, false, BudgetBlock.Social, "Contribuições"),
        new("OTHER_EXPENSE", "Outras despesas", CategoryKind.Expense, false, BudgetBlock.Social, "Outras"),

        // Investment
        new("INV_RESERVE", "Reserva de emergência", CategoryKind.Expense, true, BudgetBlock.Investment, "Curto prazo"),
        new("INV_TRAVEL_US", "Viagem para EUA", CategoryKind.Expense, false, BudgetBlock.Investment, "Curto prazo"),
        new("INV_REAL_ESTATE", "Consórcio Imobiliário", CategoryKind.Expense, false, BudgetBlock.Investment, "Médio prazo"),
        new("INV_CAR", "Consórcio automóvel", CategoryKind.Expense, false, BudgetBlock.Investment, "Médio prazo"),
        new("INV_FUNCEF", "Aposentadoria - Previdência Privada - FUNCEF", CategoryKind.Expense, false, BudgetBlock.Investment, "Longo prazo"),
        new("INV_STOCKS", "Aposentadoria - Ações", CategoryKind.Expense, false, BudgetBlock.Investment, "Longo prazo"),
        new("INV_COLLEGE", "Faculdade filhos", CategoryKind.Expense, false, BudgetBlock.Investment, "Longo prazo"),
        new("INV_EUROPE", "Viagem para Europa", CategoryKind.Expense, false, BudgetBlock.Investment, "Longo prazo"),

        // Habitação
        new("HOUSING_RENT", "Aluguel", CategoryKind.Expense, true, BudgetBlock.Essential, "Habitação"),
        new("HOUSING_DEPOSIT", "Seguro fiança", CategoryKind.Expense, true, BudgetBlock.Essential, "Habitação"),
        new("HOUSING_CONDO", "Condomínio", CategoryKind.Expense, true, BudgetBlock.Essential, "Habitação"),
        new("HOUSING_IPTU", "IPTU e Taxa de lixo", CategoryKind.Expense, true, BudgetBlock.Essential, "Habitação"),
        new("HOUSING_CLEANER", "Diarista", CategoryKind.Expense, true, BudgetBlock.Essential, "Habitação"),
        new("HOUSING_POWER", "Energia", CategoryKind.Expense, true, BudgetBlock.Essential, "Habitação"),
        new("HOUSING_WATER", "Água", CategoryKind.Expense, true, BudgetBlock.Essential, "Habitação"),
        new("HOUSING_PHONE", "Telefone Fixo / Celular", CategoryKind.Expense, true, BudgetBlock.Essential, "Habitação"),
        new("HOUSING_GAS", "Gás", CategoryKind.Expense, true, BudgetBlock.Essential, "Habitação"),
        new("HOUSING_INTERNET", "Internet / TV", CategoryKind.Expense, true, BudgetBlock.Essential, "Habitação"),
        new("HOUSING_MAINT", "Manutenção e consertos", CategoryKind.Expense, true, BudgetBlock.Essential, "Habitação"),

        // Transporte
        new("TRANSPORT_FUEL", "Combustível", CategoryKind.Expense, true, BudgetBlock.Essential, "Transporte"),
        new("TRANSPORT_PARK", "Estacionamento", CategoryKind.Expense, true, BudgetBlock.Essential, "Transporte"),
        new("TRANSPORT_WASH", "Lavagem do carro", CategoryKind.Expense, false, BudgetBlock.Essential, "Transporte"),
        new("TRANSPORT_PASS", "Vale Transporte", CategoryKind.Expense, true, BudgetBlock.Essential, "Transporte"),
        new("TRANSPORT_UBER", "Uber", CategoryKind.Expense, false, BudgetBlock.Essential, "Transporte"),
        new("TRANSPORT_INSURE", "Seguros do carro/bicicleta", CategoryKind.Expense, true, BudgetBlock.Essential, "Transporte"),
        new("TRANSPORT_MAINT", "Carro / Bicicletas (manutenção)", CategoryKind.Expense, true, BudgetBlock.Essential, "Transporte"),

        // Saúde
        new("HEALTH_PLAN", "Plano de Saúde - Mensalidade", CategoryKind.Expense, true, BudgetBlock.Essential, "Saúde"),
        new("HEALTH_COPAY", "Plano de Saúde - Participação", CategoryKind.Expense, true, BudgetBlock.Essential, "Saúde"),
        new("HEALTH_MEDS", "Medicamentos", CategoryKind.Expense, true, BudgetBlock.Essential, "Saúde"),
        new("HEALTH_DENTIST", "Dentista", CategoryKind.Expense, true, BudgetBlock.Essential, "Saúde"),

        // Educação
        new("EDU_SCHOOL", "Escola", CategoryKind.Expense, true, BudgetBlock.Essential, "Educação"),
        new("EDU_MATERIAL", "Material escolar", CategoryKind.Expense, true, BudgetBlock.Essential, "Educação"),
        new("EDU_EXTRA", "Cursos extras", CategoryKind.Expense, false, BudgetBlock.Essential, "Educação"),
        new("EDU_MBA", "MBA", CategoryKind.Expense, false, BudgetBlock.Essential, "Educação"),
        new("EDU_ENGLISH", "Inglês", CategoryKind.Expense, false, BudgetBlock.Essential, "Educação"),
        new("EDU_BOOKS", "Livros", CategoryKind.Expense, false, BudgetBlock.Essential, "Educação"),

        // Impostos
        new("TAX_IPVA", "IPVA", CategoryKind.Expense, true, BudgetBlock.Essential, "Impostos"),
        new("TAX_IRRF", "IRRF", CategoryKind.Expense, true, BudgetBlock.Essential, "Impostos"),
        new("TAX_FINES", "Multas", CategoryKind.Expense, false, BudgetBlock.Essential, "Impostos"),

        // Dívidas
        new("DEBT_1", "Dívida 1", CategoryKind.Expense, true, BudgetBlock.Essential, "Dívidas"),
        new("DEBT_2", "Dívida 2", CategoryKind.Expense, true, BudgetBlock.Essential, "Dívidas"),
        new("DEBT_3", "Dívida 3", CategoryKind.Expense, true, BudgetBlock.Essential, "Dívidas"),
        new("DEBT_INTEREST", "Juros", CategoryKind.Expense, true, BudgetBlock.Essential, "Dívidas"),
        new("DEBT_BANK_FEE", "Tarifas bancárias - manutenção de conta", CategoryKind.Expense, true, BudgetBlock.Essential, "Dívidas"),
        new("DEBT_LIFE", "Seguro de vida", CategoryKind.Expense, true, BudgetBlock.Essential, "Dívidas"),

        // Alimentação
        new("FOOD_MARKET", "Supermercado", CategoryKind.Expense, true, BudgetBlock.Social, "Alimentação"),
        new("FOOD_FAIR", "Feira", CategoryKind.Expense, true, BudgetBlock.Social, "Alimentação"),
        new("FOOD_BAKERY", "Padaria", CategoryKind.Expense, true, BudgetBlock.Social, "Alimentação"),
        new("FOOD_RESTAURANT", "Restaurante", CategoryKind.Expense, false, BudgetBlock.Social, "Alimentação"),
        new("FOOD_SNACKS", "Lanches", CategoryKind.Expense, false, BudgetBlock.Social, "Alimentação"),

        // Vestuário
        new("CLOTHES", "Roupas", CategoryKind.Expense, false, BudgetBlock.Social, "Vestuário"),
        new("SHOES", "Calçados", CategoryKind.Expense, false, BudgetBlock.Social, "Vestuário"),
        new("UNIFORM", "Uniforme", CategoryKind.Expense, false, BudgetBlock.Social, "Vestuário"),

        // Cuidados pessoais
        new("CARE_HAIR", "Cabeleireiro", CategoryKind.Expense, false, BudgetBlock.Social, "Cuidados pessoais"),
        new("CARE_NAIL", "Salão de beleza - Unha", CategoryKind.Expense, false, BudgetBlock.Social, "Cuidados pessoais"),
        new("CARE_AESTHETIC", "Salão de beleza - Procedimento estético", CategoryKind.Expense, false, BudgetBlock.Social, "Cuidados pessoais"),
        new("CARE_GYM", "Academia - Musculação", CategoryKind.Expense, false, BudgetBlock.Social, "Cuidados pessoais"),

        // Lazer
        new("LEISURE_GIFTS", "Presentes", CategoryKind.Expense, false, BudgetBlock.Social, "Lazer"),
        new("LEISURE_TRAVEL", "Viagens extras (congressos / Família)", CategoryKind.Expense, false, BudgetBlock.Social, "Lazer"),
        new("LEISURE_CLUB", "Mensalidade - Clubes", CategoryKind.Expense, false, BudgetBlock.Social, "Lazer"),
        new("LEISURE_GAME", "Game", CategoryKind.Expense, false, BudgetBlock.Social, "Lazer"),
        new("LEISURE_ALLOWANCE", "Mesadas", CategoryKind.Expense, false, BudgetBlock.Social, "Lazer"),

        // Assinaturas
        new("SUB_STREAM", "Netflix/Prime/Disney", CategoryKind.Expense, false, BudgetBlock.Social, "Assinaturas"),
        new("SUB_AI", "Inteligência Artificial", CategoryKind.Expense, false, BudgetBlock.Social, "Assinaturas"),
        new("SUB_NEWS", "Jornais e TV", CategoryKind.Expense, false, BudgetBlock.Social, "Assinaturas"),
        new("SUB_CLOUD", "Internet - Armazenamento", CategoryKind.Expense, false, BudgetBlock.Social, "Assinaturas"),

        // Furniture / other social
        new("FURNITURE", "Móveis e utensílios", CategoryKind.Expense, false, BudgetBlock.Social, "Outras"),

        new("SALARY", "Salário", CategoryKind.Income, false),
        new("EXTRA_INCOME", "Renda extra", CategoryKind.Income, false),
        new("INVESTMENT_INCOME", "Rendimentos", CategoryKind.Income, false),
        new(Transfer, "Transferência", CategoryKind.Transfer, false)
    ];

    public static bool IsForbiddenName(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return normalized.StartsWith("descontos sobre renda", StringComparison.Ordinal)
               || normalized.StartsWith("desconto sobre renda", StringComparison.Ordinal);
    }
}
