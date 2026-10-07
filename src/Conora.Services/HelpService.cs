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
            "Valor que realmente chega para usar no mês: renda bruta menos INSS e IR. O dízimo não é descontado da renda; é despesa em Contribuições/Doações."),
        new("competencia", "Competência",
            "Mês de referência no formato ano-mês (ex.: 2026-10). Orçamento, lançamentos e diagnóstico usam a competência para organizar o histórico."),
        new("planejado", "Planejado",
            "Quanto você pretende gastar (ou investir) naquela linha no mês."),
        new("realizado", "Realizado",
            "Quanto já foi gasto ou lançado. Nasce só de lançamentos e compras no cartão, nunca de digitação direta no orçamento."),
        new("essencial", "Essencial",
            "Gastos sem os quais a vida do mês não funciona (moradia, transporte, saúde, etc.). A reserva usa esse marcador."),
        new("social", "Social",
            "Gastos de convivência e escolha (lazer, vestuário, assinaturas). São os primeiros a ajustar quando o mês aperta."),
        new("investimento", "Investimento (bloco)",
            "Linhas de curto, médio e longo prazo no orçamento (reserva, consórcio, previdência)."),
        new("projeto-de-vida", "Projeto de vida",
            "Objetivo com meta, prazo e valor acumulado. Os aportes saem da sua conta; não há simulação de rendimento."),
        new("tenant", "Sua conta (isolamento)",
            "Cada login vê e grava só os próprios dados. No grupo familiar, a soma do orçamento é leitura compartilhada; contas e lançamentos continuam pessoais."),
        new("alerta-70", "Alertas 70% / 100%",
            "Abaixo de 70% está ok; de 70% a 99% atenção; 100% no limite; acima estourou. Planejado zero com gasto conta como estourou.")
    ];

    private static readonly IReadOnlyList<HelpModuleResponse> Modules =
    [
        new("inicio", "Início",
            "Painel do mês: receitas, despesas, resultado e alertas. Em grupo familiar, os totais somam os dois membros.",
            [
                new("Receitas", "Soma da renda gastável do diagnóstico e receitas extras do mês."),
                new("Despesas", "Despesas, contribuições e compras no cartão na competência."),
                new("Resultado", "Receitas menos despesas do mês."),
                new("Alertas", "Avisos de orçamento, limite de cartão e mês negativo.")
            ]),

        new("lancamentos", "Lançamentos",
            "Registra o movimento do dia a dia. É a fonte do realizado no orçamento.",
            [
                new("Tipo", "Despesa, receita, transferência, contribuição ou aporte em projeto."),
                new("Valor", "Valor do lançamento, sempre com duas casas decimais."),
                new("Data", "Dia em que a movimentação ocorreu."),
                new("Descrição", "Texto livre que identifica o lançamento."),
                new("Conta", "Conta de onde sai ou para onde entra o dinheiro."),
                new("Conta de destino", "Só em transferência: conta que recebe."),
                new("Categoria", "Classificação da despesa ou receita."),
                new("Parcelas", "Divide o valor em partes iguais, uma por mês (ex.: R$ 300 em 3 → R$ 100 × 3)."),
                new("Repetir por meses", "Repete o valor cheio a cada mês. Não use junto com parcelas."),
                new("Competência", "Mês em que o lançamento entra no orçamento e nos relatórios.")
            ]),

        new("orcamento", "Orçamento",
            "Planejado x realizado do mês, em três blocos, com visão do ano.",
            [
                new("Renda gastável", "Soma dos líquidos do diagnóstico do mês (base do percentual dos blocos)."),
                new("Fontes de renda", "Nomes e líquidos vindos do Diagnóstico."),
                new("Modo simples", "Mostra uma linha por grupo (Habitação, Alimentação, etc.)."),
                new("Modo detalhado", "Mostra cada subcategoria (aluguel, condomínio, energia…)."),
                new("Bloco Investimentos", "Curto, médio e longo prazo."),
                new("Bloco Essenciais", "Habitação, transporte, saúde, educação, impostos, dívidas."),
                new("Bloco Sociais", "Alimentação, vestuário, cuidados, lazer, assinaturas."),
                new("% do bloco", "Realizado do bloco dividido pela renda gastável."),
                new("Planejado (linha)", "Meta que você edita; o realizado vem dos lançamentos."),
                new("Resultado do mês", "Renda gastável menos o realizado total."),
                new("Visão do ano", "Planejado e realizado lado a lado de janeiro a dezembro."),
                new("Copiar mês anterior", "Traz o modo e os planejados do mês passado.")
            ]),

        new("contas", "Contas",
            "Contas bancárias e de caixa. Cada pessoa vê só as suas, mesmo em grupo familiar.",
            [
                new("Nome", "Como a conta aparece nos lançamentos."),
                new("Tipo", "Conta corrente, caixa ou outro."),
                new("Saldo inicial", "Saldo ao cadastrar a conta."),
                new("Saldo atual", "Atualizado pelos lançamentos e transferências."),
                new("Arquivar", "Esconde a conta sem apagar o histórico."),
                new("Transferir", "Move valor entre duas contas suas.")
            ]),

        new("cartoes", "Cartões",
            "Cartões de crédito, compras parceladas e faturas. Pessoal de cada usuário.",
            [
                new("Nome", "Identificação do cartão."),
                new("Limite", "Limite total do cartão."),
                new("Dia de fechamento", "Dia em que a fatura fecha."),
                new("Dia de vencimento", "Dia de pagamento da fatura."),
                new("Conta de pagamento", "Conta usada ao pagar a fatura."),
                new("Compra", "Valor, data, parcelas, categoria e descrição."),
                new("Parcelas da compra", "Divide a compra na fatura mês a mês."),
                new("Fatura", "Total do mês; pagar gera lançamento de pagamento (não conta como despesa de novo).")
            ]),

        new("categorias", "Categorias",
            "Classificam lançamentos e linhas de orçamento. Há categorias do sistema e as suas.",
            [
                new("Nome", "Rótulo da categoria."),
                new("Tipo", "Despesa, receita ou transferência."),
                new("Bloco", "Investimento, essencial ou social — usado no orçamento."),
                new("Grupo", "Agrupador (Habitação, Alimentação…). No modo simples o orçamento soma por grupo."),
                new("Essencial", "Marca gasto essencial para reserva e organização."),
                new("Ativa", "Categoria inativa não aparece para novos lançamentos."),
                new("Do sistema", "Não pode ser renomeada ou excluída; só as personalizadas.")
            ]),

        new("projetos", "Projetos de vida",
            "Metas com prazo e aportes. Pessoal ou do grupo familiar.",
            [
                new("Nome", "Nome do objetivo."),
                new("Meta", "Valor que você quer atingir."),
                new("Prazo", "Data alvo (opcional)."),
                new("Acumulado", "Soma dos aportes já feitos."),
                new("Progresso", "Percentual acumulado sobre a meta."),
                new("Escopo pessoal", "Só você vê e edita."),
                new("Escopo do grupo", "Aparece para os dois; só o dono edita ou apaga. Exige grupo ativo."),
                new("Aporte", "Lançamento que sai da sua conta e aumenta o acumulado.")
            ]),

        new("patrimonio", "Patrimônio",
            "Bens, dívidas e visão de patrimônio líquido.",
            [
                new("Nome", "Identificação do item."),
                new("Tipo", "Ativo (bem) ou passivo (dívida)."),
                new("Valor", "Valor atual do bem ou da dívida."),
                new("Patrimônio líquido", "Ativos menos passivos.")
            ]),

        new("membros", "Membros e grupo familiar",
            "Perfil, convite e gestão do casal/família. Cada um mantém login próprio.",
            [
                new("Nome de exibição", "Como você aparece no grupo e nos avisos."),
                new("Convite (e-mail)", "Envia link por e-mail (SES). Só o e-mail convidado consegue entrar."),
                new("Link do convite", "Abre o Conora; não entra no grupo sozinho. Token de uso único e com prazo."),
                new("Aceitar convite", "Liga você ao grupo de quem convidou e mostra a tela de boas-vindas."),
                new("Sair / Remover", "Qualquer lado pode desfazer; o grupo se encerra."),
                new("Avisos", "Notificações no app, por exemplo quando o convite é aceito."),
                new("O que soma no grupo", "Orçamento, início e relatórios do mês."),
                new("O que continua pessoal", "Contas, cartões, lançamentos, diagnóstico e perfil.")
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
            "Resumo do mês, exportações e atalhos para outras funções.",
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
                new("Cadastros", "Guia recolhida no menu: Contas, Cartões, Categorias, Projetos, Patrimônio e Membros."),
                new("Recolher menu", "No desktop largo, deixa só os ícones para ganhar espaço."),
                new("Tema", "Ícone de sol (claro) ou lua (escuro)."),
                new("Versão", "Número do app no rodapé do menu (ex.: 0.1.2).")
            ]),

        new("configuracoes", "Configurações",
            "Preferências de cada usuário. Atalho no rodapé do menu, acima do perfil.",
            [
                new("Tema", "Claro ou escuro; fica neste aparelho."),
                new("Menu lateral", "Expandido ou só ícones no desktop."),
                new("Modo padrão do orçamento", "Simples ou detalhado quando o mês ainda não tem orçamento."),
                new("Nome de exibição", "Como você aparece no grupo e nos avisos; gravado na conta."),
                new("Atalhos", "Grupo, plano e central de ajuda.")
            ])
    ];

    public HelpResponse Get() => new(Steps, Glossary, Modules);

    public HelpStepResponse? GetStep(string key)
        => Steps.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public HelpModuleResponse? GetModule(string key)
        => Modules.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}
