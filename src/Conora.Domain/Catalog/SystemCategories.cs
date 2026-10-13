using Conora.Domain.Enums;

namespace Conora.Domain.Catalog;

public sealed record SystemCategoryDefinition(
    string Code,
    string Name,
    CategoryLevel Level,
    CategorySection Section,
    string? ParentCode,
    int SortOrder);

/// <summary>
/// Official chart of accounts (Docs/Plano de contas e Patrimônio Conora _ Revisado.xlsx).
/// Structural masters Orçamento, Despesa and Patrimônio stay so the Raio-X tree still has a home.
/// </summary>
public static class SystemCategories
{
    public const string Tithe = "DISC_TITHE";
    public const string Offerings = "DISC_OFFERINGS";
    public const string AssetsInUse = "Bens de uso";
    public const string AssetsNotInUse = "Bens de não uso";

    /// <summary>Retired system codes whose postings move to the official account.</summary>
    public static readonly IReadOnlyDictionary<string, string> Replacements = new Dictionary<string, string>
    {
        ["INC_ROLE"] = "INC_OTHER_ITEM",
        ["INC_INCORP"] = "INC_BONUS",
        ["DISC_UNION"] = "DISC_OTHER",
        ["LIFE_US"] = "LIFE_TRIP",
        ["LIFE_FUNCEF"] = "LIFE_RETIRE",
        ["LIFE_STOCKS"] = "LIFE_RETIRE",
        ["LIFE_LIFE_INS"] = "LIFE_LONG_OTHER",
        ["ESS_INCENTIVE"] = "ESS_EDU_OTHER",
        ["ESS_DEBT_1"] = "ESS_DEBT_OTHER",
        ["ESS_DEBT_2"] = "ESS_DEBT_OTHER",
        ["ESS_DEBT_3"] = "ESS_DEBT_OTHER",
        ["ESS_VAC_ADV"] = "ESS_DEBT_OTHER",
        ["ESS_13TH_ADV"] = "ESS_DEBT_OTHER",
        ["ESS_MARTIAL"] = "ESS_SPORT",
        ["ESS_VOLLEY"] = "ESS_SPORT",
        ["ESS_FOOTBALL"] = "ESS_SPORT",
        ["ESS_SALON_HAIR"] = "ESS_SALON",
        ["SOC_REWARD"] = "SOC_LIVE",
        ["LIAB_OVER_BANESTES"] = "LIAB_OVERDRAFT",
        ["LIAB_OVER_CEF"] = "LIAB_OVERDRAFT",
        ["LIAB_CONS_BANESTES"] = "LIAB_CONSIGNED",
        ["LIAB_CONS_CEF"] = "LIAB_CONSIGNED",
    };

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

        Root("BUDGET", "Orçamento", CategorySection.Budget);
        Root("PATRIMONY", "Patrimônio", CategorySection.Patrimony);
        Group("EXPENSE", "Despesa", CategorySection.Expense, "BUDGET");


        Group("INC", "Renda", CategorySection.Income, "BUDGET");
        Group("INC_WORK", "Renda do trabalho", CategorySection.Income, "INC");
        Leaf("INC_SALARY", "Salário bruto", CategorySection.Income, "INC_WORK");
        Leaf("INC_PROLABORE", "Pró-labore", CategorySection.Income, "INC_WORK");
        Leaf("INC_13TH", "13º salário", CategorySection.Income, "INC_WORK");
        Leaf("INC_VACATION", "Férias", CategorySection.Income, "INC_WORK");
        Leaf("INC_OVERTIME", "Horas extras", CategorySection.Income, "INC_WORK");
        Leaf("INC_COMMISSION", "Comissões", CategorySection.Income, "INC_WORK");
        Leaf("INC_BONUS", "Bonificações e gratificações", CategorySection.Income, "INC_WORK");
        Leaf("INC_NIGHT", "Adicional noturno", CategorySection.Income, "INC_WORK");
        Leaf("INC_SENIORITY", "Adicional por tempo de serviço", CategorySection.Income, "INC_WORK");
        Leaf("INC_PLR", "Participação nos lucros (PLR)", CategorySection.Income, "INC_WORK");
        Group("INC_BENEFIT", "Benefícios recebidos", CategorySection.Income, "INC");
        Leaf("INC_MEAL", "Vale-alimentação / vale-refeição", CategorySection.Income, "INC_BENEFIT");
        Leaf("INC_REMOTE", "Ajuda de custo", CategorySection.Income, "INC_BENEFIT");
        Leaf("INC_TRANSPORT_AID", "Auxílio-transporte", CategorySection.Income, "INC_BENEFIT");
        Leaf("INC_CHILDCARE", "Auxílio-creche", CategorySection.Income, "INC_BENEFIT");
        Leaf("INC_BENEFIT_OTHER", "Outros benefícios recebidos", CategorySection.Income, "INC_BENEFIT");
        Group("INC_OTHER", "Outras receitas", CategorySection.Income, "INC");
        Leaf("INC_EXTRA", "Renda extra / trabalho autônomo", CategorySection.Income, "INC_OTHER");
        Leaf("INC_FREELANCE", "Freelance / prestação de serviço", CategorySection.Income, "INC_OTHER");
        Leaf("INC_RENT_IN", "Aluguel recebido", CategorySection.Income, "INC_OTHER");
        Leaf("INC_PENSION", "Pensão / aposentadoria recebida", CategorySection.Income, "INC_OTHER");
        Leaf("INC_ALIMONY_IN", "Pensão alimentícia recebida", CategorySection.Income, "INC_OTHER");
        Leaf("INC_IR_REFUND", "Restituição de Imposto de Renda", CategorySection.Income, "INC_OTHER");
        Leaf("INC_REFUND", "Reembolso recebido", CategorySection.Income, "INC_OTHER");
        Leaf("INC_ASSET_SALE", "Venda de bem", CategorySection.Income, "INC_OTHER");
        Leaf("INC_GIFT_IN", "Presente / doação recebida", CategorySection.Income, "INC_OTHER");
        Leaf("INC_OTHER_ITEM", "Outras receitas", CategorySection.Income, "INC_OTHER");

        Group("DISC", "Descontos sobre renda", CategorySection.Discount, "EXPENSE");
        Group("DISC_MANDATORY", "Descontos obrigatórios", CategorySection.Discount, "DISC");
        Leaf("DISC_INSS", "INSS", CategorySection.Discount, "DISC_MANDATORY");
        Leaf("DISC_IR", "Imposto de Renda Retido na Fonte (IRRF)", CategorySection.Discount, "DISC_MANDATORY");
        Leaf("DISC_ALIMONY", "Pensão alimentícia descontada em folha", CategorySection.Discount, "DISC_MANDATORY");
        Leaf("DISC_OTHER", "Outros descontos legais", CategorySection.Discount, "DISC_MANDATORY");

        Group("LIFE", "Projetos de vida", CategorySection.LifeProject, "EXPENSE");
        Group("LIFE_SHORT", "Curto prazo", CategorySection.LifeProject, "LIFE");
        Leaf("LIFE_RESERVE", "Reserva de emergência", CategorySection.LifeProject, "LIFE_SHORT");
        Leaf("LIFE_ANNUAL", "Reserva para despesas anuais", CategorySection.LifeProject, "LIFE_SHORT");
        Leaf("LIFE_TRIP", "Viagem", CategorySection.LifeProject, "LIFE_SHORT");
        Leaf("LIFE_ELECTRONICS", "Compra de eletrônicos", CategorySection.LifeProject, "LIFE_SHORT");
        Leaf("LIFE_COURSE", "Curso / capacitação", CategorySection.LifeProject, "LIFE_SHORT");
        Leaf("LIFE_EVENT", "Evento ou comemoração", CategorySection.LifeProject, "LIFE_SHORT");
        Leaf("LIFE_SMALL_RENOV", "Pequena reforma", CategorySection.LifeProject, "LIFE_SHORT");
        Leaf("LIFE_SHORT_OTHER", "Outro projeto de curto prazo", CategorySection.LifeProject, "LIFE_SHORT");
        Group("LIFE_MID", "Médio prazo", CategorySection.LifeProject, "LIFE");
        Leaf("LIFE_DOWNPAY", "Entrada de imóvel", CategorySection.LifeProject, "LIFE_MID");
        Leaf("LIFE_HOME_BUY", "Compra de imóvel", CategorySection.LifeProject, "LIFE_MID");
        Leaf("LIFE_CAR_BUY", "Compra / troca de automóvel", CategorySection.LifeProject, "LIFE_MID");
        Leaf("LIFE_REAL_ESTATE", "Consórcio imobiliário", CategorySection.LifeProject, "LIFE_MID");
        Leaf("LIFE_CAR", "Consórcio de veículo", CategorySection.LifeProject, "LIFE_MID");
        Leaf("LIFE_GRAD", "Curso superior / pós-graduação", CategorySection.LifeProject, "LIFE_MID");
        Leaf("LIFE_WEDDING", "Casamento", CategorySection.LifeProject, "LIFE_MID");
        Leaf("LIFE_BIG_TRIP", "Viagem de grande porte", CategorySection.LifeProject, "LIFE_MID");
        Leaf("LIFE_RENOV", "Reforma de imóvel", CategorySection.LifeProject, "LIFE_MID");
        Leaf("LIFE_BUSINESS", "Capital para negócio", CategorySection.LifeProject, "LIFE_MID");
        Leaf("LIFE_MID_OTHER", "Outro projeto de médio prazo", CategorySection.LifeProject, "LIFE_MID");
        Group("LIFE_LONG", "Longo prazo", CategorySection.LifeProject, "LIFE");
        Leaf("LIFE_RETIRE", "Aposentadoria", CategorySection.LifeProject, "LIFE_LONG");
        Leaf("LIFE_FI", "Independência financeira", CategorySection.LifeProject, "LIFE_LONG");
        Leaf("LIFE_COLLEGE", "Educação dos filhos", CategorySection.LifeProject, "LIFE_LONG");
        Leaf("LIFE_RENTAL_PROPERTY", "Compra de imóvel para renda", CategorySection.LifeProject, "LIFE_LONG");
        Leaf("LIFE_WEALTH", "Construção de patrimônio", CategorySection.LifeProject, "LIFE_LONG");
        Leaf("LIFE_LEGACY", "Sucessão / legado familiar", CategorySection.LifeProject, "LIFE_LONG");
        Leaf("LIFE_LONG_OTHER", "Outro projeto de longo prazo", CategorySection.LifeProject, "LIFE_LONG");

        Group("ESS", "Essencial", CategorySection.Essential, "EXPENSE");
        Group("ESS_HOUSING", "Habitação", CategorySection.Essential, "ESS");
        Leaf("ESS_RENT", "Aluguel", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_MORTGAGE_PMT", "Prestação do financiamento imobiliário", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_CONDO", "Condomínio", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_IPTU", "IPTU", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_GARBAGE", "Taxa de lixo", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_HOME_INS", "Seguro residencial", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_DEPOSIT", "Seguro-fiança", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_POWER", "Energia elétrica", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_WATER", "Água e esgoto", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_GAS", "Gás", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_INTERNET", "Internet residencial", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_PHONE", "Telefone fixo", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_MOBILE", "Celular", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_CLEANER", "Diarista / limpeza", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_MAINT", "Manutenção e reparos da casa", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_FURNITURE", "Móveis e utensílios domésticos", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_CLEANING", "Materiais de limpeza", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_MOVING", "Mudança / frete residencial", CategorySection.Essential, "ESS_HOUSING");
        Leaf("ESS_HOUSING_OTHER", "Outras despesas de habitação", CategorySection.Essential, "ESS_HOUSING");
        Group("ESS_FOOD", "Alimentação", CategorySection.Essential, "ESS");
        Leaf("ESS_MARKET", "Supermercado", CategorySection.Essential, "ESS_FOOD");
        Leaf("ESS_FAIR", "Hortifruti / feira", CategorySection.Essential, "ESS_FOOD");
        Leaf("ESS_BAKERY", "Padaria", CategorySection.Essential, "ESS_FOOD");
        Leaf("ESS_BUTCHER", "Açougue / peixaria", CategorySection.Essential, "ESS_FOOD");
        Leaf("ESS_WORK_MEAL", "Refeição no trabalho", CategorySection.Essential, "ESS_FOOD");
        Leaf("ESS_LUNCHBOX", "Marmita / refeição do dia a dia", CategorySection.Essential, "ESS_FOOD");
        Leaf("ESS_WATER_BOTTLE", "Água mineral", CategorySection.Essential, "ESS_FOOD");
        Leaf("ESS_FOOD_OTHER", "Outras despesas de alimentação", CategorySection.Essential, "ESS_FOOD");
        Group("ESS_CLOTHES", "Vestuário", CategorySection.Essential, "ESS");
        Leaf("ESS_CLOTHES_ITEM", "Roupas", CategorySection.Essential, "ESS_CLOTHES");
        Leaf("ESS_SHOES", "Calçados", CategorySection.Essential, "ESS_CLOTHES");
        Leaf("ESS_UNDERWEAR", "Roupas íntimas", CategorySection.Essential, "ESS_CLOTHES");
        Leaf("ESS_ACCESSORIES", "Acessórios", CategorySection.Essential, "ESS_CLOTHES");
        Leaf("ESS_TAILOR", "Costura e ajustes", CategorySection.Essential, "ESS_CLOTHES");
        Leaf("ESS_LAUNDRY", "Lavanderia", CategorySection.Essential, "ESS_CLOTHES");
        Leaf("ESS_CLOTHES_OTHER", "Outras despesas de vestuário", CategorySection.Essential, "ESS_CLOTHES");
        Group("ESS_HEALTH", "Saúde", CategorySection.Essential, "ESS");
        Leaf("ESS_HEALTH_PLAN", "Plano de saúde - mensalidade", CategorySection.Essential, "ESS_HEALTH");
        Leaf("ESS_HEALTH_COPAY", "Plano de saúde - coparticipação", CategorySection.Essential, "ESS_HEALTH");
        Leaf("ESS_DOCTOR", "Consulta médica", CategorySection.Essential, "ESS_HEALTH");
        Leaf("ESS_EXAMS", "Exames e diagnósticos", CategorySection.Essential, "ESS_HEALTH");
        Leaf("ESS_MEDS", "Medicamentos", CategorySection.Essential, "ESS_HEALTH");
        Leaf("ESS_DENTIST", "Dentista", CategorySection.Essential, "ESS_HEALTH");
        Leaf("ESS_DENTAL_TREAT", "Tratamento odontológico", CategorySection.Essential, "ESS_HEALTH");
        Leaf("ESS_THERAPY", "Psicólogo / terapia", CategorySection.Essential, "ESS_HEALTH");
        Leaf("ESS_PHYSIO", "Fisioterapia", CategorySection.Essential, "ESS_HEALTH");
        Leaf("ESS_GLASSES", "Óculos e lentes de contato", CategorySection.Essential, "ESS_HEALTH");
        Leaf("ESS_VACCINE", "Vacinas", CategorySection.Essential, "ESS_HEALTH");
        Leaf("ESS_HEALTH_GEAR", "Equipamentos e itens de saúde", CategorySection.Essential, "ESS_HEALTH");
        Leaf("ESS_HEALTH_OTHER", "Outras despesas de saúde", CategorySection.Essential, "ESS_HEALTH");
        Group("ESS_EDU", "Educação e profissão", CategorySection.Essential, "ESS");
        Leaf("ESS_SCHOOL", "Escola / mensalidade escolar", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_COLLEGE", "Faculdade / universidade", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_MBA", "Pós-graduação / MBA", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_MATERIAL", "Material escolar", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_UNIFORM", "Uniforme escolar", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_BOOKS", "Livros e materiais didáticos", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_ENGLISH", "Curso de idiomas", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_COURSES", "Cursos e capacitações", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_CERT", "Certificações profissionais", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_COUNCIL", "Conselho / anuidade profissional", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_CONGRESS", "Congresso / evento profissional", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_WORK_SW", "Software / ferramenta de trabalho", CategorySection.Essential, "ESS_EDU");
        Leaf("ESS_EDU_OTHER", "Outras despesas de educação e profissão", CategorySection.Essential, "ESS_EDU");
        Group("ESS_TRANSPORT", "Transporte", CategorySection.Essential, "ESS");
        Leaf("ESS_FUEL", "Combustível", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_TRANSIT", "Transporte público", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_PASS", "Vale-transporte", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_UBER", "Aplicativo de transporte", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_TAXI", "Táxi", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_PARK", "Estacionamento", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_TOLL", "Pedágio", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_IPVA", "IPVA", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_LICENSE", "Licenciamento do veículo", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_CAR_INS", "Seguro do carro", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_MOTO_INS", "Seguro da moto", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_CAR_MAINT", "Manutenção do carro", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_MOTO_MAINT", "Manutenção da moto", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_BIKE_MAINT", "Manutenção da bicicleta", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_TIRES", "Pneus", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_WASH", "Lavagem do veículo", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_TOW", "Reboque / assistência", CategorySection.Essential, "ESS_TRANSPORT");
        Leaf("ESS_TRANSPORT_OTHER", "Outras despesas de transporte", CategorySection.Essential, "ESS_TRANSPORT");
        Group("ESS_TAX", "Impostos", CategorySection.Essential, "ESS");
        Leaf("ESS_IRRF", "Imposto de Renda - pagamento complementar / DARF", CategorySection.Essential, "ESS_TAX");
        Leaf("ESS_FEES_TAX", "Taxas e tributos pessoais", CategorySection.Essential, "ESS_TAX");
        Leaf("ESS_MEI", "Contribuição MEI / DAS pessoal", CategorySection.Essential, "ESS_TAX");
        Leaf("ESS_TAX_OTHER", "Outras obrigações tributárias", CategorySection.Essential, "ESS_TAX");
        Group("ESS_DEBT", "Dívidas e despesas financeiras", CategorySection.Essential, "ESS");
        Leaf("ESS_LOAN", "Empréstimo pessoal", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_CONSIGNED", "Empréstimo consignado", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_OVERDRAFT", "Cheque especial", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_CAR_FIN", "Financiamento de veículo", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_MORTGAGE", "Financiamento imobiliário", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_RENEG", "Renegociação de dívida", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_CARD_INST", "Parcelamento de cartão", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_CARD_INT", "Juros do cartão de crédito", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_INTEREST", "Juros e encargos", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_FINES", "Multas por atraso", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_BANK_FEE", "Tarifas bancárias", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_IOF", "IOF e tarifas financeiras", CategorySection.Essential, "ESS_DEBT");
        Leaf("ESS_DEBT_OTHER", "Outras dívidas / despesas financeiras", CategorySection.Essential, "ESS_DEBT");
        Group("ESS_CARE", "Cuidados pessoais", CategorySection.Essential, "ESS");
        Leaf("ESS_HAIR", "Barbearia / cabeleireiro", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_SALON", "Salão de beleza", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_NAIL", "Unhas / manicure / pedicure", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_WAX", "Depilação", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_AESTHETIC", "Procedimento estético", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_COSMETICS", "Cosméticos e higiene pessoal", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_GYM", "Academia / musculação", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_SPORT", "Esporte / atividade física", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_TRAINER", "Personal trainer", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_MASSAGE", "Massagem / bem-estar", CategorySection.Essential, "ESS_CARE");
        Leaf("ESS_CARE_OTHER", "Outros cuidados pessoais", CategorySection.Essential, "ESS_CARE");

        Group("SOC", "Social", CategorySection.Social, "EXPENSE");
        Group("SOC_GIFTS", "Presentes e comemorações", CategorySection.Social, "SOC");
        Leaf("SOC_FAMILY", "Presentes para família", CategorySection.Social, "SOC_GIFTS");
        Leaf("SOC_FRIENDS", "Presentes para amigos", CategorySection.Social, "SOC_GIFTS");
        Leaf("SOC_COLLEAGUES", "Presentes para colegas de trabalho", CategorySection.Social, "SOC_GIFTS");
        Leaf("SOC_BIRTHDAY", "Aniversários e comemorações", CategorySection.Social, "SOC_GIFTS");
        Leaf("SOC_HOLIDAY", "Datas comemorativas", CategorySection.Social, "SOC_GIFTS");
        Leaf("SOC_GIFTS_OTHER", "Outros presentes", CategorySection.Social, "SOC_GIFTS");
        Group("SOC_DONATE", "Doações e contribuições", CategorySection.Social, "SOC");
        Leaf(Tithe, "Dízimo", CategorySection.Social, "SOC_DONATE");
        Leaf(Offerings, "Oferta religiosa", CategorySection.Social, "SOC_DONATE");
        Leaf("SOC_NGO", "Doação para instituição / ONG", CategorySection.Social, "SOC_DONATE");
        Leaf("SOC_CHARITY", "Ação beneficente", CategorySection.Social, "SOC_DONATE");
        Leaf("SOC_HELP", "Ajuda financeira a terceiros", CategorySection.Social, "SOC_DONATE");
        Leaf("SOC_DONATE_OTHER", "Outras doações", CategorySection.Social, "SOC_DONATE");
        Group("SOC_LIVE", "Lazer e convivência", CategorySection.Social, "SOC");
        Leaf("SOC_RESTAURANT", "Restaurante", CategorySection.Social, "SOC_LIVE");
        Leaf("SOC_SNACKS", "Lanches / cafeteria", CategorySection.Social, "SOC_LIVE");
        Leaf("SOC_DELIVERY", "Delivery por lazer", CategorySection.Social, "SOC_LIVE");
        Leaf("SOC_CINEMA", "Cinema / teatro", CategorySection.Social, "SOC_LIVE");
        Leaf("SOC_SHOWS", "Shows / eventos", CategorySection.Social, "SOC_LIVE");
        Leaf("SOC_OUTINGS", "Passeios", CategorySection.Social, "SOC_LIVE");
        Leaf("SOC_TRAVEL", "Viagens de lazer", CategorySection.Social, "SOC_LIVE");
        Leaf("SOC_CLUB", "Clubes / mensalidades", CategorySection.Social, "SOC_LIVE");
        Leaf("SOC_HOBBY", "Hobbies", CategorySection.Social, "SOC_LIVE");
        Leaf("SOC_GAMES", "Jogos e entretenimento", CategorySection.Social, "SOC_LIVE");
        Leaf("SOC_ALLOWANCE", "Mesada dos filhos", CategorySection.Social, "SOC_LIVE");
        Leaf("SOC_PARTY", "Confraternizações", CategorySection.Social, "SOC_LIVE");
        Leaf("SOC_LEISURE", "Outras despesas de lazer", CategorySection.Social, "SOC_LIVE");
        Group("SOC_SUB", "Assinaturas e serviços digitais", CategorySection.Social, "SOC");
        Leaf("SOC_STREAM", "Streaming de vídeo", CategorySection.Social, "SOC_SUB");
        Leaf("SOC_MUSIC", "Streaming de música", CategorySection.Social, "SOC_SUB");
        Leaf("SOC_GAME", "Games / assinaturas de jogos", CategorySection.Social, "SOC_SUB");
        Leaf("SOC_NEWS", "Jornais e notícias", CategorySection.Social, "SOC_SUB");
        Leaf("SOC_MAGAZINE", "Revistas", CategorySection.Social, "SOC_SUB");
        Leaf("SOC_CLOUD", "Armazenamento em nuvem", CategorySection.Social, "SOC_SUB");
        Leaf("SOC_APPS", "Aplicativos e softwares", CategorySection.Social, "SOC_SUB");
        Leaf("SOC_AI", "Inteligência artificial", CategorySection.Social, "SOC_SUB");
        Leaf("SOC_BENEFITS_CLUB", "Clube de benefícios", CategorySection.Social, "SOC_SUB");
        Leaf("SOC_SUB_OTHER", "Outras assinaturas", CategorySection.Social, "SOC_SUB");


        Group("ASSET", "Ativo", CategorySection.Asset, "PATRIMONY");
        Group("ASSET_USE", "Bens de uso", CategorySection.Asset, "ASSET");
        Leaf("ASSET_CASH", "Dinheiro em espécie", CategorySection.Asset, "ASSET_USE");
        Leaf("ASSET_CHECKING", "Conta corrente", CategorySection.Asset, "ASSET_USE");
        Leaf("ASSET_DIGITAL", "Conta digital", CategorySection.Asset, "ASSET_USE");
        Leaf("ASSET_WALLET", "Carteira digital / saldo em app", CategorySection.Asset, "ASSET_USE");
        Group("ASSET_NONUSE", "Bens de não uso", CategorySection.Asset, "ASSET");
        Leaf("ASSET_SAVINGS", "Poupança", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_CDB", "CDB / RDB", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_TREASURY", "Tesouro Direto", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_LCI", "LCI / LCA", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_FUNDS", "Fundos de investimento", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_STOCKS", "Ações", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_ETF", "ETFs", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_FII", "Fundos imobiliários (FIIs)", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_PENSION", "Previdência privada", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_CRYPTO", "Criptoativos", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_FOREIGN", "Investimentos no exterior", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_FGTS", "FGTS", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_INVEST_OTHER", "Outros investimentos", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_HOME", "Imóvel - residência principal", CategorySection.Asset, "ASSET_USE");
        Leaf("ASSET_RENTAL", "Imóvel - aluguel / renda", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_LEISURE_PROP", "Imóvel - lazer", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_LAND", "Terreno / lote", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_COMMERCIAL", "Imóvel comercial", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_PROPERTY_OTHER", "Outro imóvel", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_CAR", "Automóvel", CategorySection.Asset, "ASSET_USE");
        Leaf("ASSET_MOTO", "Motocicleta", CategorySection.Asset, "ASSET_USE");
        Leaf("ASSET_BIKE", "Bicicleta de valor relevante", CategorySection.Asset, "ASSET_USE");
        Leaf("ASSET_BOAT", "Embarcação", CategorySection.Asset, "ASSET_USE");
        Leaf("ASSET_EQUIPMENT", "Máquinas / equipamentos", CategorySection.Asset, "ASSET_USE");
        Leaf("ASSET_JEWELRY", "Joias / colecionáveis", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_GOODS_OTHER", "Outros bens relevantes", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_EQUITY", "Participação em empresa / quotas", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_RECEIVABLE", "Crédito a receber", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_CONSORTIUM", "Consórcio - valor acumulado", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_COURT", "Precatório / direito creditório", CategorySection.Asset, "ASSET_NONUSE");
        Leaf("ASSET_RIGHTS_OTHER", "Outros direitos", CategorySection.Asset, "ASSET_NONUSE");

        Group("LIAB", "Passivo", CategorySection.Liability, "PATRIMONY");
        Group("LIAB_SHORT", "Cartões e crédito de curto prazo", CategorySection.Liability, "LIAB");
        Leaf("LIAB_CARD", "Cartão de crédito - saldo devedor", CategorySection.Liability, "LIAB_SHORT");
        Leaf("LIAB_OVERDRAFT", "Cheque especial", CategorySection.Liability, "LIAB_SHORT");
        Leaf("LIAB_REVOLVING", "Crédito rotativo", CategorySection.Liability, "LIAB_SHORT");
        Leaf("LIAB_INVOICE_INST", "Parcelamento de fatura", CategorySection.Liability, "LIAB_SHORT");
        Group("LIAB_LOANS", "Empréstimos", CategorySection.Liability, "LIAB");
        Leaf("LIAB_PERSONAL", "Empréstimo pessoal", CategorySection.Liability, "LIAB_LOANS");
        Leaf("LIAB_CONSIGNED", "Empréstimo consignado", CategorySection.Liability, "LIAB_LOANS");
        Leaf("LIAB_SECURED", "Empréstimo com garantia", CategorySection.Liability, "LIAB_LOANS");
        Leaf("LIAB_INFORMAL", "Empréstimo informal / familiar", CategorySection.Liability, "LIAB_LOANS");
        Leaf("LIAB_LOAN_OTHER", "Outros empréstimos", CategorySection.Liability, "LIAB_LOANS");
        Group("LIAB_FIN", "Financiamentos", CategorySection.Liability, "LIAB");
        Leaf("LIAB_MORTGAGE", "Financiamento imobiliário", CategorySection.Liability, "LIAB_FIN");
        Leaf("LIAB_CAR_FIN", "Financiamento de automóvel", CategorySection.Liability, "LIAB_FIN");
        Leaf("LIAB_MOTO_FIN", "Financiamento de motocicleta", CategorySection.Liability, "LIAB_FIN");
        Leaf("LIAB_STUDENT", "Financiamento estudantil", CategorySection.Liability, "LIAB_FIN");
        Leaf("LIAB_FIN_OTHER", "Outros financiamentos", CategorySection.Liability, "LIAB_FIN");
        Group("LIAB_OBLIG", "Obrigações e dívidas", CategorySection.Liability, "LIAB");
        Leaf("LIAB_TAX", "Impostos / tributos a pagar", CategorySection.Liability, "LIAB_OBLIG");
        Leaf("LIAB_RENEG", "Renegociação de dívida", CategorySection.Liability, "LIAB_OBLIG");
        Leaf("LIAB_LAWSUIT", "Dívida judicial", CategorySection.Liability, "LIAB_OBLIG");
        Leaf("LIAB_ALIMONY", "Pensão / obrigação vencida", CategorySection.Liability, "LIAB_OBLIG");
        Leaf("LIAB_OTHER", "Outras obrigações", CategorySection.Liability, "LIAB_OBLIG");

        return list;
    }

    public static string SectionLabel(CategorySection section) => section switch
    {
        CategorySection.Budget => "Orçamento",
        CategorySection.Expense => "Despesa",
        CategorySection.Patrimony => "Patrimônio",
        CategorySection.Income => "Renda",
        CategorySection.Discount => "Descontos sobre renda",
        CategorySection.LifeProject => "Projetos de vida",
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
