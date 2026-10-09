using Conora.Services.Contracts;

namespace Conora.Services;

/// <summary>Static help center content: onboarding steps, glossary and per-module field guide.</summary>
public sealed class HelpService
{
    private static readonly IReadOnlyList<HelpStepResponse> Steps =
    [
        new(1, "diagnosticar", "Diagnosticar",
            "Descubra para onde seu dinheiro vai hoje. Informe a renda bruta, o INSS e o IR para ver a renda gastável do mês."),
        new(2, "organizar", "Organizar",
            "Cadastre suas contas, cartões e o plano de contas. Cada centavo precisa ter um lugar para ser registrado."),
        new(3, "planejar", "Planejar",
            "Em Orçamento, defina o planejado do mês (simples ou detalhado). O Raio-X só acompanha previsto × realizado."),
        new(4, "registrar", "Registrar",
            "Lance receitas e despesas no dia a dia. A movimentação é a única fonte da verdade do aplicativo."),
        new(5, "acompanhar", "Acompanhar",
            "No Raio-X, abra as contas sintéticas e os grupos para ver totais e lançamentos. Os alertas avisam aos 70%, aos 100% e quando o limite é ultrapassado."),
        new(6, "corrigir", "Corrigir",
            "Ajuste o orçamento do mês aberto quando a realidade mudar e feche o mês para travar o histórico."),
        new(7, "construir", "Construir",
            "Crie projetos com prazo e início do aporte. O Conora gera a parcela sugerida no orçamento até o prazo.")
    ];

    private static readonly IReadOnlyList<HelpTermResponse> Glossary =
    [
        new("renda-gastavel", "Renda gastável",
            "Valor que realmente chega para usar no mês: renda bruta menos INSS e IR. O dízimo não é descontado da renda; é despesa em Contribuições/Doações."),
        new("competencia", "Competência",
            "Mês de referência no formato ano-mês (ex.: 2026-10). Orçamento, Raio-X, lançamentos e diagnóstico usam a competência."),
        new("raio-x", "Raio-X",
            "Visão do mês: previsto, realizado e variação, na ordem do plano de contas. Começa fechado nas contas sintéticas; o + abre grupos e depois os lançamentos."),
        new("orcamento-cadastro", "Orçamento (cadastro)",
            "Tela para informar o valor planejado de cada conta, salvar, copiar o mês anterior e ver o ano."),
        new("inicio-aporte", "Início do aporte",
            "Primeira competência em que o projeto entra no orçamento. Padrão: mês seguinte. Pode ser qualquer mês entre o atual e o prazo."),
        new("parcela-sugerida", "Parcela sugerida",
            "Meta dividida pelos meses do início do aporte até o prazo (inclusive). Esse valor vira uma linha de orçamento em cada mês."),
        new("horizonte", "Horizonte",
            "Curto, médio ou longo prazo. Vem da conta do plano (grupo pai), não de um campo no projeto."),
        new("planejado", "Planejado",
            "Quanto você pretende gastar (ou investir) naquela linha no mês."),
        new("realizado", "Realizado",
            "Quanto já foi gasto ou lançado. Nasce só de lançamentos e compras no cartão, nunca de digitação direta no Raio-X."),
        new("essencial", "Essencial",
            "Gastos sem os quais a vida do mês não funciona (moradia, transporte, saúde, etc.). A reserva usa esse marcador."),
        new("social", "Social",
            "Gastos de convivência e escolha (lazer, vestuário, assinaturas). São os primeiros a ajustar quando o mês aperta."),
        new("investimento", "Investimento (bloco)",
            "Linhas de curto, médio e longo prazo no orçamento (reserva, consórcio, previdência)."),
        new("projeto-de-vida", "Projeto de vida",
            "Objetivo com meta, prazo, início do aporte e conta do plano. Só um projeto ativo por conta. Progresso = acumulado / meta (só leitura)."),
        new("tenant", "Sua conta (isolamento)",
            "Cada pessoa tem login e dados próprios. Contas bancárias, cartões e lançamentos são individuais. No grupo familiar, só algumas telas mostram a soma dos dois — sem misturar o dinheiro de cada um."),
        new("grupo-familiar", "Grupo familiar",
            "Vínculo entre duas contas Conora (ex.: casal). Serve para ver orçamento, início e relatórios somados. Não cria conta bancária compartilhada nem permite editar o lançamento do outro."),
        new("alerta-70", "Alertas 70% / 100%",
            "Abaixo de 70% está ok; de 70% a 99% atenção; 100% no limite; acima estourou. Planejado zero com gasto conta como estourou.")
    ];

    private static readonly IReadOnlyList<HelpModuleResponse> Modules =
    [
        new("membros", "Grupo familiar — vincular e desvincular",
            "Cada usuário tem a própria conta Conora. O grupo só une a visão de algumas telas; os registros de cada um continuam separados.",
            [
                new("Visão geral", "Vocês são duas contas independentes. Cada um lança na própria conta, no próprio cartão e nas próprias contas do plano. O grupo não mistura saldos nem deixa um editar o lançamento do outro."),
                new("O que fica pessoal", "Contas bancárias, cartões, lançamentos, diagnóstico de renda, WhatsApp, importação e perfil. Só você vê e altera."),
                new("O que fica em comum (só leitura somada)", "Início (totais do mês), Raio-X planejado × realizado e relatórios do mês — somam os dois membros. Projetos de vida podem ser pessoais ou do grupo (quando marcados assim)."),
                new("Como vincular (convidar)", "Abra Cadastros → Membros. Informe o e-mail da outra pessoa e envie o convite. Ela precisa já ter (ou criar) login no Conora com esse e-mail."),
                new("Como aceitar o convite", "A pessoa abre o link do e-mail, entra com a conta do e-mail convidado e confirma. O vínculo é criado; quem convidou recebe aviso no app."),
                new("Regras do convite", "O link é de uso único, com prazo. Outro e-mail não entra. Se o convite expirar ou for cancelado, envie outro."),
                new("Como desvincular (sair)", "Em Membros, use Sair do grupo. Seu orçamento e telas voltam a ser só pessoais. O grupo se encerra para os dois."),
                new("Como desvincular (remover o outro)", "Quem está no grupo pode Remover o outro membro. O efeito é o mesmo: o grupo acaba."),
                new("Cancelar convite pendente", "Se o outro ainda não aceitou, cancele o convite em Membros. O link deixa de valer."),
                new("Nome de exibição", "Em Perfil (menu da conta) ou em Membros: como você aparece no grupo e nos avisos."),
                new("Uma pessoa, um grupo", "Cada conta fica em um grupo por vez. Aceitar outro convite não cria segundo grupo paralelo.")
            ]),

        new("inicio", "Início",
            "Painel do mês: receitas, despesas, resultado, alertas e projetos de vida. Em grupo familiar, os totais somam os dois membros.",
            [
                new("Atalhos", "Raio-X, Patrimônio, Projetos de vida e Lançar."),
                new("Projetos de vida", "Três barras horizontais: Curto, Médio e Longo. O progresso agrega os projetos daquele horizonte. Clique abre o dashboard filtrado."),
                new("Receitas", "Soma da renda gastável do diagnóstico e receitas extras do mês."),
                new("Despesas", "Despesas, contribuições e compras no cartão na competência."),
                new("Resultado", "Receitas menos despesas do mês."),
                new("Alertas", "Avisos de orçamento, limite de cartão e mês negativo.")
            ]),

        new("lancamentos", "Lançamentos",
            "Registra o movimento do dia a dia. É a fonte do realizado no Raio-X.",
            [
                new("Tipo", "Despesa, receita, transferência, contribuição ou aporte em projeto."),
                new("Valor", "Valor do lançamento, sempre com duas casas decimais."),
                new("Data", "Dia em que a movimentação ocorreu."),
                new("Descrição", "Texto livre que identifica o lançamento."),
                new("Conta bancária", "Conta de onde sai ou para onde entra o dinheiro."),
                new("Conta de destino", "Só em transferência: conta que recebe."),
                new("Conta do plano", "Busca e agrupa por seção e grupo. O lançamento entra na conta analítica."),
                new("Insert (web)", "No computador, Insert abre o formulário de novo lançamento. Não vale no app nativo. Não dispara se o foco estiver em um campo de texto."),
                new("Excluir", "Lixeira vermelha. Pede confirmação."),
                new("Competência", "Mês em que o lançamento entra no orçamento e nos relatórios.")
            ]),

        new("raio-x", "Raio-X",
            "Listagem do mês na ordem do plano de contas. Só visão — o cadastro do planejado fica em Orçamento.",
            [
                new("Estado inicial", "Tudo fechado: só as contas sintéticas (raiz) aparecem, com previsto, realizado e variação."),
                new("Expandir", "O + abre o grupo (2º nível). Outro + abre os lançamentos daquele ramo."),
                new("Totais", "Sintéticas e grupos somam as contas de baixo."),
                new("Lançamento", "Clique na linha do lançamento para abrir o registro em Lançamentos, na mesma competência e conta."),
                new("Receita", "No topo: receita prevista e recebida do diagnóstico.")
            ]),

        new("orcamento", "Orçamento",
            "Cadastro do planejado do mês.",
            [
                new("Renda gastável", "Soma dos líquidos do diagnóstico do mês (base do percentual dos blocos)."),
                new("Contas", "Cada conta analítica, na ordem do plano: seções fixas e, dentro delas, grupos e itens em ordem alfabética. Grupos começam fechados."),
                new("Busca", "Filtra a lista pelo nome da conta e abre o ramo encontrado."),
                new("Salvar", "Grava os valores planejados da competência. Cancelar descarta o rascunho."),
                new("Copiar mês anterior", "Traz os planejados do mês passado. Pergunta antes de sobrescrever."),
                new("Repetir", "Na visão do ano da conta: copia o previsto cheio para os meses seguintes."),
                new("Parcelar", "Na visão do ano da conta: divide o valor total a partir deste mês."),
                new("Visão do ano", "Planejado e realizado de janeiro a dezembro. Toque no nome da conta.")
            ]),

        new("contas", "Contas",
            "Contas bancárias. Cada pessoa cadastra e vê só as suas, mesmo em grupo familiar.",
            [
                new("Descrição", "Nome da conta no app (ex.: BB salário, Banestes casa)."),
                new("Tipo de conta", "Corrente, poupança ou investimento."),
                new("Banco", "Código COMPE da instituição (ex.: 001 Banco do Brasil, 021 Banestes)."),
                new("Agência", "Número da agência."),
                new("Número da conta", "Número da conta sem o dígito."),
                new("Dígito verificador", "Dígito da conta."),
                new("Saldo inicial", "Saldo ao cadastrar a conta."),
                new("Saldo atual", "Atualizado pelos lançamentos e transferências."),
                new("Arquivar", "Esconde a conta sem apagar o histórico."),
                new("Transferir", "Move valor entre duas contas suas — não é receita nem despesa do mês.")
            ]),

        new("cartoes", "Cartões",
            "Cartões de crédito, compras parceladas e faturas. Pessoal de cada usuário.",
            [
                new("Nome", "Identificação do cartão."),
                new("Limite", "Limite total do cartão."),
                new("Dia de fechamento", "Dia em que a fatura fecha."),
                new("Dia de vencimento", "Dia de pagamento da fatura."),
                new("Conta de pagamento", "Conta usada ao pagar a fatura."),
                new("Compra", "Valor, data, parcelas, conta do plano e descrição."),
                new("Parcelas da compra", "Divide a compra na fatura mês a mês."),
                new("Fatura", "Total do mês; pagar gera lançamento de pagamento (não conta como despesa de novo).")
            ]),

        new("plano-de-contas", "Plano de contas",
            "O lançamento entra na conta analítica. As contas de cima (Receita, Desconto, Essencial…) só somam.",
            [
                new("Numeração", "Número só de tela (1, 1.1, 2.1), gerado pelo sistema. Você não edita. Criar ou excluir uma conta renumera os irmãos. Não altera lançamentos."),
                new("Conta sintética", "As sete contas de cima e os grupos (Habitação, Curto prazo…). Não recebem valor."),
                new("Conta analítica", "Linha que recebe o lançamento (Salário, Aluguel…)."),
                new("Padrão", "Conta do sistema: não renomeia nem exclui."),
                new("Sua conta", "Analítica que você cria sob um grupo ou sob a conta, quando não há grupo."),
                new("Desativar", "Tira a conta dos seletores e mantém o histórico.")
            ]),

        new("projetos", "Projetos de vida",
            "Metas com prazo, início do aporte e conta do plano. Pessoal ou do grupo familiar.",
            [
                new("Dashboard", "Barras por horizonte, filtro Todos/Curto/Médio/Longo e lista. Clique no card abre o detalhe."),
                new("Nome", "Nome do objetivo."),
                new("Descrição detalhada", "Texto livre do projeto (opcional). Aparece no detalhe e em preview no dashboard."),
                new("Conta do plano", "Define o horizonte (curto/médio/longo). Só contas de Projetos de vida ainda sem projeto."),
                new("Meta", "Valor que você quer atingir."),
                new("Prazo", "Competência alvo. Obrigatório e não pode ser antes do início do aporte."),
                new("Início do aporte", "Primeiro mês no orçamento. Padrão: mês seguinte. Faixa: mês atual até o prazo."),
                new("Parcela sugerida", "Preview só leitura: meta ÷ meses (início até o prazo, inclusive)."),
                new("Orçamento gerado", "Ao criar ou alterar, o Conora grava essa parcela em cada mês do intervalo. Excluir o projeto remove essas linhas."),
                new("Progresso", "Acumulado / meta. Só leitura."),
                new("Filtro da lista", "Na Home, as barras Curto/Médio/Longo abrem o dashboard já filtrado."),
                new("Escopo pessoal", "Só você vê e edita."),
                new("Escopo do grupo", "Aparece para os dois; só o dono edita ou apaga. Exige grupo ativo."),
                new("Aporte", "Lançamento que sai da sua conta e aumenta o acumulado. Feito no detalhe do projeto.")
            ]),

        new("patrimonio", "Patrimônio",
            "Bens, dívidas e visão de patrimônio líquido.",
            [
                new("Nome", "Identificação do item."),
                new("Tipo", "Ativo (bem) ou passivo (dívida)."),
                new("Valor", "Valor atual do bem ou da dívida."),
                new("Patrimônio líquido", "Ativos menos passivos.")
            ]),

        new("diagnostico", "Diagnóstico de renda",
            "Fontes de renda do mês e cálculo da renda gastável.",
            [
                new("Nome da fonte", "Ex.: salário, renda extra."),
                new("Competência", "Mês do diagnóstico."),
                new("Bruto", "Renda antes dos descontos."),
                new("INSS", "Desconto de INSS."),
                new("IR", "Imposto de renda retido."),
                new("Dízimo", "Informativo; no orçamento vira Contribuições/Doações, não desconto de renda."),
                new("Líquido / renda gastável", "Bruto menos INSS e IR. Base do orçamento.")
            ]),

        new("relatorios", "Relatórios",
            "Resumo do mês, exportações e links para outras funções.",
            [
                new("Resumo do mês", "Receitas, despesas e resultado da competência."),
                new("Por categoria", "Quanto foi gasto em cada categoria."),
                new("Comparação", "Diferença em relação ao mês anterior."),
                new("Exportar CSV", "Baixa o movimento ou o resumo em planilha."),
                new("LGPD", "Exportar seus dados ou excluir a conta.")
            ]),

        new("importar", "Importar extrato",
            "Traz lançamentos de arquivo CSV ou OFX para revisão antes de gravar.",
            [
                new("Arquivo", "CSV ou OFX do banco."),
                new("Pré-visualização", "Linhas lidas para você conferir."),
                new("Confirmar", "Grava os lançamentos escolhidos nas suas contas.")
            ]),

        new("mes", "Fechar mês",
            "Trava a competência para não alterar o histórico sem querer.",
            [
                new("Competência", "Mês que será fechado ou reaberto."),
                new("Fechar", "Impede novos lançamentos naquele mês."),
                new("Reabrir", "Exige motivo; só quem fechou (dono) reabre."),
                new("No grupo", "Cada pessoa fecha o próprio mês; a visão somada avisa se um ainda está aberto.")
            ]),

        new("plano", "Plano e assinatura",
            "Situação da assinatura do workspace.",
            [
                new("Plano", "Mensal ou anual."),
                new("Status", "Ativo, expirado ou somente leitura."),
                new("Validade", "Até quando o plano permite gravar.")
            ]),

        new("whatsapp", "WhatsApp",
            "Funcionalidade futura — ainda não disponível nesta versão.",
            [
                new("Status", "Em breve: lançamentos por mensagem no WhatsApp."),
            ]),

        new("ia", "Perguntar à IA",
            "Tire dúvidas sobre o seu mês com base nos seus dados.",
            [
                new("Pergunta", "Texto livre sobre orçamento, gastos ou o mês."),
                new("Competência", "Mês de referência da pergunta (opcional)."),
                new("Resposta", "Orientação gerada; não substitui o lançamento nem o orçamento.")
            ]),

        new("menu", "Menu e preferências",
            "Navegação e aparência do aplicativo.",
            [
                new("Menu principal", "Início, Lançamentos, Raio-X, Orçamento e Relatórios."),
                new("Cadastros", "Guia recolhida no menu: Contas, Cartões, Plano de contas, Projetos, Patrimônio e Membros."),
                new("Menu da conta", "Avatar no topo: Perfil, tema (Claro/Escuro), Plano, Central de ajuda e Sair."),
                new("Recolher menu", "No desktop largo, deixa só os ícones para ganhar espaço."),
                new("Tema", "No menu da conta: se o tema atual for claro, aparece Escuro — e o contrário."),
                new("Versão", "Número do app no rodapé do menu (ex.: 0.1.2).")
            ]),

        new("configuracoes", "Configurações",
            "Preferências de cada usuário. Abra pelo item Perfil no menu da conta.",
            [
                new("Tema", "Claro ou escuro; fica neste aparelho. Também pode trocar pelo menu da conta."),
                new("Menu lateral", "Expandido ou só ícones no desktop."),
                new("Nome de exibição", "Como você aparece no grupo e nos avisos; gravado na conta.")
            ])
    ];

    public HelpResponse Get() => new(Steps, Glossary, Modules);

    public HelpStepResponse? GetStep(string key)
        => Steps.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public HelpModuleResponse? GetModule(string key)
        => Modules.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}
