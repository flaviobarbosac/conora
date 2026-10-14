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
            "Cadastre suas contas, cartões e as categorias (Orçamento e Patrimônio). Cada centavo precisa ter um lugar para ser registrado."),
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
            "Visão do mês: previsto, realizado e % do previsto. Na raiz mostra Renda, Despesa e Patrimônio; o clique desce a cadeia até o item lançado."),
        new("orcamento-cadastro", "Orçamento (cadastro)",
            "Tela para informar o valor planejado de cada conta, salvar, copiar o mês anterior e ver o ano."),
        new("inicio-aporte", "Início do aporte",
            "Primeira competência em que o projeto entra no orçamento. Padrão: mês seguinte. Pode ser qualquer mês entre o atual e o prazo."),
        new("parcela-sugerida", "Parcela sugerida",
            "Meta dividida pelos meses do início do aporte até o prazo (inclusive). Esse valor vira uma linha de orçamento em cada mês."),
        new("horizonte", "Horizonte",
            "Curto, médio ou longo prazo. Vem da categoria (grupo pai), não de um campo no projeto."),
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
            "Objetivo com meta, prazo, início do aporte e conta do plano. Vários projetos podem usar a mesma conta. Progresso = acumulado / meta (só leitura)."),
        new("tenant", "Sua conta (isolamento)",
            "Cada pessoa tem login e dados próprios. Contas bancárias, cartões e lançamentos são individuais. No grupo familiar, só algumas telas mostram a soma dos dois — sem misturar o dinheiro de cada um."),
        new("grupo-familiar", "Grupo familiar",
            "Vínculo entre duas contas Conora (ex.: casal). Serve para ver orçamento, início e relatórios somados. Não cria conta bancária compartilhada nem permite editar o lançamento do outro."),
        new("alerta-70", "Alertas 70% / 100%",
            "Abaixo de 70% está ok; de 70% a 99% atenção; 100% no limite; acima estourou. Planejado zero com gasto conta como estourou."),
        new("escopo-familia", "Escopo Família (categoria)",
            "Conta do plano marcada como Família. Entra no Raio-X e no Orçamento da visão consolidada do casal. É o padrão ao criar categoria (modelo Família-primeiro)."),
        new("escopo-pessoal", "Escopo Pessoal (categoria)",
            "Conta do plano só sua. Não entra no consolidado familiar. Use quando aquele gasto ou receita não deve aparecer na visão do outro membro."),
        new("familia-primeiro", "Família-primeiro",
            "O Conora assume Família como padrão: categorias novas nascem Família; Orçamento e Raio-X abrem na visão Família. Pessoal é a exceção (opt-out), não a regra."),
        new("responsavel-orcamento", "Responsável pelo orçamento",
            "No grupo familiar, só essa pessoa edita o planejado consolidado. Quem está sozinho (sem grupo) edita o próprio orçamento normalmente. A responsabilidade pode ser transferida em Membros."),
        new("emails-conora", "E-mails do Conora",
            "Preferência em Configurações. Ligado: você recebe avisos e convites. Desligado: o Conora para de enviar e-mails do sistema (opt-out). Bounce/reclamação do provedor continua protegendo o endereço."),
        new("visao-familia-pessoal", "Visão Família / Pessoal",
            "Seletor em Orçamento e Raio-X quando você está em grupo. Família (consolidado) soma o que for de escopo Família; Pessoal mostra só as suas contas marcadas como Pessoal."),
        new("onboarding", "Onboarding (jornada Raio X)",
            "Passos iniciais após o cadastro: diagnosticar, organizar, planejar, registrar, acompanhar, corrigir e construir. Concluir marca a jornada como feita na conta.")
    ];

    private static readonly IReadOnlyList<HelpModuleResponse> Modules =
    [
        new("membros", "Grupo familiar — vincular, orçamento e desvincular",
            "Cada usuário tem a própria conta Conora. O grupo une a visão consolidada (Família-primeiro); os registros de cada um continuam separados. Lançamentos e contas bancárias nunca se misturam.",
            [
                new("Visão geral", "Vocês são duas contas independentes. Cada um lança na própria conta, no próprio cartão e nas próprias contas do plano. O grupo não mistura saldos nem deixa um editar o lançamento do outro."),
                new("Modelo Família-primeiro", "Categorias novas nascem com escopo Família. Orçamento e Raio-X abrem na visão Família (consolidado). Marque como Pessoal só o que não deve entrar na soma do casal."),
                new("O que fica pessoal", "Contas bancárias, cartões, lançamentos, diagnóstico de renda, WhatsApp, importação, perfil e preferências (tema, e-mails, ritmo). Só você vê e altera."),
                new("O que fica em comum (visão somada)", "Início (totais do mês), Raio-X e Orçamento na visão Família, relatórios do mês — somam o que for de escopo Família. Projetos de vida podem ser pessoais ou do grupo."),
                new("Responsável pelo orçamento", "Quem cria o grupo (ou quem recebe a transferência) é o responsável. Só essa pessoa edita o planejado consolidado enquanto houver dois membros. Sem grupo (sozinho), você edita o próprio orçamento sem bloqueio."),
                new("Transferir responsabilidade", "Em Membros, o responsável pode pedir transferência ao outro. O outro precisa aceitar. Enquanto a transferência estiver pendente, dá para cancelar."),
                new("Como vincular (convidar)", "Abra Cadastros → Membros. Informe o e-mail da outra pessoa e envie o convite. Ela precisa já ter (ou criar) login no Conora com esse e-mail. O e-mail sai pelo remetente do Conora (se a pessoa não desligou e-mails nas Configurações)."),
                new("Como aceitar o convite", "A pessoa abre o link do e-mail, entra com a conta do e-mail convidado e confirma. O vínculo é criado; quem convidou recebe aviso no app e, se habilitado, e-mail."),
                new("Regras do convite", "O link é de uso único, com prazo, preso ao e-mail convidado. Outro e-mail não entra. Se expirar ou for cancelado, envie outro. Segundo clique no mesmo link informa que já está no grupo."),
                new("Como desvincular (sair)", "Em Membros, use Sair do grupo. Seu orçamento e telas voltam a ser só seus. O grupo se encerra para os dois. Projetos do grupo ficam com quem criou."),
                new("Como desvincular (remover o outro)", "Quem está no grupo pode Remover o outro membro. O efeito é o mesmo: o grupo acaba."),
                new("Cancelar convite pendente", "Se o outro ainda não aceitou, cancele o convite em Membros. O link deixa de valer."),
                new("Nome de exibição", "Em Perfil (menu da conta) ou em Membros: como você aparece no grupo e nos avisos."),
                new("Uma pessoa, um grupo", "Cada conta fica em um grupo por vez. Aceitar outro convite não cria segundo grupo paralelo."),
                new("Exclusão de conta (LGPD)", "Se você for o responsável pelo orçamento e ainda houver outro membro ativo, o Conora pede para transferir a responsabilidade ou encerrar o grupo antes de excluir a conta.")
            ]),

        new("inicio", "Início",
            "Painel do mês: pizzas de renda e despesa, comparação em barra, e os blocos Orçamento, Patrimônio e Projetos de vida. Em grupo familiar, os totais somam o que for de escopo Família (modelo Família-primeiro).",
            [
                new("Renda e despesa", "Duas pizzas com legenda: detalhamento da renda e da despesa realizadas no mês."),
                new("Comparativo", "Barra horizontal com o total de renda frente ao total de despesa."),
                new("Visão macro", "Três blocos: orçamento (previsto × realizado), patrimônio (ativo e passivo) e projetos de vida (horizontes)."),
                new("Grupo familiar", "Com vínculo ativo, os totais refletem a visão consolidada (categorias Família). Contas Pessoal do outro membro não entram na sua soma."),
                new("Projetos de vida", "Três barras: verde curto, amarelo médio, azul longo. Vermelho só quando o acumulado passa a meta (Acima da meta). Clique abre a lista filtrada."),
                new("Alertas", "Avisos de orçamento, limite de cartão e mês negativo.")
            ]),

        new("lancamentos", "Lançamentos",
            "Registra o movimento do dia a dia. É a fonte do realizado no Raio-X.",
            [
                new("Tipo", "Despesa, renda, transferência, contribuição ou aporte em projeto."),
                new("Valor", "Valor do lançamento, sempre com duas casas decimais."),
                new("Data", "Dia em que a movimentação ocorreu."),
                new("Descrição", "Texto livre que identifica o lançamento."),
                new("Conta bancária", "Conta de onde sai ou para onde entra o dinheiro."),
                new("Conta de destino", "Só em transferência: conta que recebe."),
                new("Conta do plano", "Busca e agrupa por seção e grupo. O lançamento entra na conta analítica."),
                new("Insert (web)", "No computador, Insert abre o formulário de novo lançamento. Não vale no app nativo. Não dispara se o foco estiver em um campo de texto."),
                new("Editar", "Clique na linha de despesa, renda ou transferência para abrir o formulário preenchido. Salvar grava; Cancelar (ou Fechar) descarta o rascunho. O tipo não muda na edição."),
                new("Excluir", "Lixeira vermelha. Pede confirmação."),
                new("Competência", "Mês em que o lançamento entra no orçamento e nos relatórios.")
            ]),

        new("raio-x", "Raio-X",
            "Listagem do mês na ordem das categorias. Só visão — o cadastro do planejado fica em Orçamento. Padrão: visão Família quando você está em grupo.",
            [
                new("Indicadores", "No topo: saldo do mês anterior, renda recebida, descontos, renda gastável, projetos de vida e saldo do mês. Toque nos do meio destaca a seção na lista."),
                new("Visão Família / Pessoal", "Com grupo ativo, o seletor Visão aparece. Família (consolidado) é o padrão e mostra o plano Família-primeiro. Pessoal filtra só as suas categorias de escopo Pessoal."),
                new("Patrimônio", "Card com ativo, passivo e patrimônio líquido (resultado, não conta)."),
                new("Projetos de vida", "Quanto foi separado no período, com atalho para Projetos."),
                new("Período", "Arraste de 1 a 24 meses até a competência escolhida."),
                new("Estado inicial", "Só o primeiro nível: Renda, Despesa e Patrimônio."),
                new("Navegar", "Clique desce a cadeia de contas. Só o item lançado (lançamento, projeto ou patrimônio) abre a tela de registro."),
                new("Totais", "Cada linha soma as contas de baixo."),
                new("% do previsto", "Com orçamento, mostra o progresso. Sem orçamento lançado, aparece 0% em amarelo. Legenda: Amarelo = sem orçamento lançado."),
                new("Sem categorias na visão", "Se a visão filtrada não tiver contas (ex.: Pessoal sem nenhuma marcada), a lista fica vazia — não fica carregando sem fim.")
            ]),

        new("orcamento", "Orçamento",
            "Cadastro do planejado do mês. Modelo Família-primeiro: abre na visão Família; Pessoal é opt-out.",
            [
                new("Renda gastável", "Soma dos líquidos do diagnóstico do mês (base do percentual dos blocos)."),
                new("Renda", "Também recebe valor planejado, além das despesas e dos outros blocos."),
                new("Visão Família / Pessoal", "Em grupo, use o seletor Visão. Família (consolidado) lista categorias de escopo Família. Pessoal lista só as suas privadas. Sozinho, o orçamento é o seu plano (padrão Família nas categorias)."),
                new("Quem pode editar", "Sozinho: você edita normalmente. Em grupo: só o responsável pelo orçamento grava o planejado consolidado. Se não for o responsável, os campos ficam bloqueados."),
                new("Lista vazia", "Se não houver categoria na visão escolhida, aparece mensagem clara (não um carregamento infinito). Em Pessoal: \"Nenhuma categoria pessoal…\". Em Família: \"Nenhuma categoria para montar o orçamento.\""),
                new("Contas", "Cada conta analítica, na ordem do plano: seções fixas e, dentro delas, grupos e itens em ordem alfabética. Grupos começam fechados."),
                new("Busca", "Filtra a lista pelo nome da conta e abre o ramo encontrado."),
                new("% do previsto", "Sem valor planejado, mostra 0% em amarelo. Legenda: Amarelo = sem orçamento lançado."),
                new("Salvar", "Grava os valores planejados da competência. Cancelar descarta o rascunho. Um aviso confirma quando gravou. Dados só persistem ao Salvar — sair com alterações pede confirmação."),
                new("Copiar mês anterior", "Traz os planejados do mês passado. Pergunta antes de sobrescrever (pode copiar só contas vazias)."),
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
                new("Saldo atual", "Atualizado pelos lançamentos."),
                new("Arquivar", "Esconde a conta sem apagar o histórico.")
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

        new("categorias", "Categorias",
            "Orçamento (o que entra e sai no mês) e Patrimônio (o que você tem e o que deve). O lançamento entra na categoria analítica; as de cima só somam. Escopo Família ou Pessoal define o que entra no consolidado.",
            [
                new("Tipo", "Combo selecionável (Orçamento, Essencial, Cuidados pessoais…). Digite para achar e filtre a árvore."),
                new("Buscar", "Campo ao lado do tipo: digite o nome (aluguel, salário…) para achar a categoria."),
                new("Numeração", "Número só de tela (1, 1.1, 2.1), gerado pelo sistema. Você não edita. Criar ou excluir renumera os irmãos. Não altera lançamentos."),
                new("Categoria sintética", "Orçamento, Despesa, Patrimônio e os grupos (Habitação, Curto prazo…). Não recebem valor."),
                new("Categoria analítica", "Linha que recebe o lançamento (Salário, Aluguel…)."),
                new("Padrão", "Categoria do sistema: não renomeia nem exclui. Contas do plano padrão também seguem o modelo Família-primeiro."),
                new("Sua categoria", "Analítica que você cria sob Renda, Descontos sobre renda, Projetos de vida, Essencial, Social, Ativo ou Passivo. Nova categoria nasce com escopo Família."),
                new("Escopo Família", "Entra no Orçamento e no Raio-X da visão Família (consolidado). Padrão para contas novas e para o plano oficial."),
                new("Escopo Pessoal", "Fica só na sua visão Pessoal. No grupo, marque Pessoal as contas que não devem entrar no Raio-X consolidado (ex.: gasto particular)."),
                new("Trocar escopo", "Em Categorias, alterne Família ↔ Pessoal na conta. A mudança vale na hora para filtros de Orçamento e Raio-X."),
                new("Desativar", "Tira a categoria dos seletores e mantém o histórico.")
            ]),

        new("projetos", "Projetos de vida",
            "Metas com prazo, início do aporte e conta do plano. Pessoal ou do grupo familiar.",
            [
                new("Dashboard", "Barras por horizonte: verde curto, amarelo médio, azul longo. Acima da meta fica vermelho, com o texto Acima da meta. Clique no card abre o detalhe."),
                new("Nome", "Nome do objetivo."),
                new("Descrição detalhada", "Texto livre do projeto (opcional). Aparece no detalhe e em preview no dashboard."),
                new("Conta do plano", "Define o horizonte (curto/médio/longo). Contas de Projetos de vida; vários projetos podem usar a mesma conta."),
                new("Meta", "Valor que você quer atingir."),
                new("Prazo", "Competência alvo. Obrigatório e não pode ser antes do início do aporte."),
                new("Início do aporte", "Primeiro mês no orçamento. Padrão: mês seguinte. Faixa: mês atual até o prazo."),
                new("Parcela sugerida", "Preview só leitura: meta ÷ meses (início até o prazo, inclusive)."),
                new("Orçamento gerado", "Ao criar ou alterar, o Conora grava essa parcela em cada mês do intervalo. Excluir o projeto remove essas linhas."),
                new("Progresso", "Acumulado / meta. Só leitura."),
                new("Filtro da lista", "Na própria tela: Todos, Curto, Médio e Longo. Na Home, as barras abrem o filtro correspondente."),
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
                new("Resumo do mês", "Renda, despesas e resultado da competência."),
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

        new("ia", "Ian",
            "Pergunte sobre os seus dados; o Ian monta um relatório com números e gráfico em outra página.",
            [
                new("Pergunta", "Texto livre, por exemplo o resumo do mês ou a evolução de um gasto."),
                new("Relatório", "Página estática com valores em cima e o gráfico escolhido embaixo."),
                new("PDF", "Na página do relatório, exporta valores e gráfico no mesmo arquivo.")
            ]),

        new("menu", "Menu e preferências",
            "Navegação e aparência do aplicativo.",
            [
                new("Menu principal", "Início, Lançamentos, Raio-X, Orçamento e Relatórios."),
                new("Cadastros", "Guia recolhida no menu: Contas, Cartões, Categorias, Projetos, Patrimônio e Membros."),
                new("Configurações", "Item no rodapé do menu lateral (ícone de engrenagem). Preferências de ritmo, e-mails, reserva e retrospectiva."),
                new("Menu da conta", "Avatar no topo: Perfil, Temas, Plano, Central de ajuda e Sair. Temas abre o submenu com todos os temas do app."),
                new("Recolher menu", "No desktop largo, deixa só os ícones para ganhar espaço."),
                new("Tema", "No menu da conta → Temas: escolha Claro, Noite, Contraste e demais opções. Fica neste aparelho."),
                new("Marca Conora", "Logo e wordmark Conora na tela de login, splash e ícones do PWA/app. Onra aparece só como assinatura da empresa."),
                new("Salvar", "Ao gravar um cadastro ou lançamento, um aviso confirma que salvou. Cancelar descarta o rascunho."),
                new("Versão", "Número do app no rodapé do menu (ex.: 0.1.2).")
            ]),

        new("configuracoes", "Configurações",
            "Preferências de cada usuário. Abra pelo item Configurações no rodapé do menu lateral.",
            [
                new("Modo discreto", "Desligado: mostra jornada e celebrações. Ligado: só números, sem celebrações."),
                new("E-mails do Conora", "Receber (padrão) ou Não receber. Desligar grava opt-out: o sistema para de enviar avisos e convites para o seu e-mail. Religar remove só o opt-out seu — não apaga bloqueios por bounce ou reclamação do provedor."),
                new("O que os e-mails cobrem", "Convites de grupo, avisos de vínculo aceito e outras mensagens transacionais do Conora. Layout com identidade visual do produto."),
                new("Meta da reserva (meses)", "Quantos meses de despesas essenciais você mira na reserva (1 a 24; padrão 6)."),
                new("Dia de fechamento do mês", "Dia da competência (1 a 31) em que você costuma fechar o mês. Separado do dia de fechamento do cartão."),
                new("Retrospectiva", "Incluir ou não valores absolutos (além de percentuais e marcos) na retrospectiva."),
                new("Salvar / Cancelar", "Só grava ao tocar Salvar. Cancelar descarta o rascunho e volta ao último estado salvo."),
                new("Conta", "Atalhos para Plano e Perfil; exportar dados (LGPD) ou excluir a conta."),
                new("Nome de exibição", "Como você aparece no grupo e nos avisos — edite em Perfil."),
                new("Tema", "Troque em Configurações indiretamente pelo menu da conta → Temas (fica neste aparelho).")
            ]),

        new("familia-primeiro", "Família-primeiro (visão consolidada)",
            "Como o Conora trata o plano de contas e as telas analíticas depois do modelo Família-primeiro.",
            [
                new("Ideia", "O casal (ou a família) vê o mesmo plano consolidado por padrão. O que for só de um fica marcado Pessoal e some da visão Família."),
                new("Categorias", "Novas contas analíticas nascem Família. Contas já existentes do plano oficial também seguem Família. Troque para Pessoal em Categorias quando precisar."),
                new("Orçamento e Raio-X", "Com grupo, o seletor Visão padrão é Família (consolidado). Pessoal mostra só o privado. Sem categorias na visão, a tela mostra estado vazio — não fica em \"Carregando…\"."),
                new("Sozinho", "Sem grupo ativo, você usa o próprio plano (categorias Família por padrão) e edita o orçamento sem precisar de responsável."),
                new("Em grupo", "Só o responsável pelo orçamento edita o planejado consolidado. O outro membro vê a visão Família em leitura para o planejado."),
                new("Projetos", "Continua à parte: ao criar o projeto, escolha Pessoal ou Família (grupo). Não é o mesmo campo do escopo da categoria."),
                new("Isolamento", "Mesmo na visão Família, ninguém edita lançamento, conta bancária ou cartão do outro. A soma é de leitura e de planejado compartilhado, não de caixa único.")
            ])
    ];

    public HelpResponse Get() => new(Steps, Glossary, Modules);

    public HelpStepResponse? GetStep(string key)
        => Steps.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public HelpModuleResponse? GetModule(string key)
        => Modules.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}
