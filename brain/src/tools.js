'use strict';

// MCP tool catalog and handlers. Each tool declares the minimum role allowed to
// see and call it: reader < suggester < admin. Knowledge tables are written
// only by brain_approve (admin), and only for a suggestion already queued.

const db = require('./db');
const { SEARCH_TYPES, quoteIdent, search } = require('./search');
const { encodeToon } = require('./toon');

const ROLE_LEVEL = { reader: 1, suggester: 2, admin: 3 };

const KNOWLEDGE_TABLES = [
  'brain_categories', 'rules', 'rules_history', 'guidelines', 'guidelines_history',
  'projects', 'project_learnings', 'error_patterns', 'artifacts', 'snippets',
  'directory_structures',
];
const SUGGESTION_TYPES = ['insert', 'update', 'delete', 'new_category'];
const AGENTS = ['bibliotecario', 'fiscal', 'orquestrador'];
const VERSIONED = { rules: 'rules_history', guidelines: 'guidelines_history' };
const VERSIONED_FK = { rules: 'rule_id', guidelines: 'guideline_id' };

function text(value) {
  return { content: [{ type: 'text', text: value }] };
}

function json(rows, emptyMessage) {
  return text(rows.length ? JSON.stringify(rows, null, 2) : emptyMessage);
}

function requireString(value, name) {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`Parâmetro "${name}" é obrigatório.`);
  return value.trim();
}

function requireId(value, name = 'id') {
  const id = Number(value);
  if (!Number.isInteger(id) || id < 1) throw new Error(`Parâmetro "${name}" deve ser um inteiro positivo.`);
  return id;
}

function validateSuggestion(s) {
  if (!s || typeof s !== 'object') throw new Error('Cada sugestão deve ser um objeto.');
  if (!SUGGESTION_TYPES.includes(s.type)) {
    throw new Error(
      `type="${s.type}" inválido. Use ${SUGGESTION_TYPES.join('/')}. A categoria da linha ` +
      '(ex.: observation/worked/failed em project_learnings) vai dentro de suggested_data.type.'
    );
  }
  if (!KNOWLEDGE_TABLES.includes(s.target_table)) {
    throw new Error(`target_table="${s.target_table}" não permitida. Use uma de: ${KNOWLEDGE_TABLES.join(', ')}.`);
  }
  const data = s.suggested_data;
  if (!data || typeof data !== 'object' || Array.isArray(data)) throw new Error('suggested_data deve ser um objeto.');
  if (s.type === 'update') {
    requireId(data.id, 'suggested_data.id');
    if (!data.set || typeof data.set !== 'object' || !Object.keys(data.set).length) {
      throw new Error('update exige suggested_data = { id, set: { coluna: valor } }.');
    }
    Object.keys(data.set).forEach(quoteIdent);
  } else if (s.type === 'delete') {
    requireId(data.id, 'suggested_data.id');
  } else {
    if (!Object.keys(data).length) throw new Error('insert exige ao menos uma coluna em suggested_data.');
    Object.keys(data).forEach(quoteIdent);
  }
  requireString(s.reason, 'reason');
}

async function applyApproved(client, sug) {
  const table = sug.target_table;
  if (!KNOWLEDGE_TABLES.includes(table)) throw new Error(`Tabela alvo não permitida: ${table}`);
  const t = quoteIdent(table);
  const data = sug.suggested_data;

  if (sug.type === 'insert' || sug.type === 'new_category') {
    const cols = Object.keys(data);
    const { rows } = await client.query(
      `INSERT INTO ${t} (${cols.map(quoteIdent).join(', ')})
       VALUES (${cols.map((_, i) => `$${i + 1}`).join(', ')}) RETURNING id`,
      cols.map((c) => data[c])
    );
    return `inserido id ${rows[0].id} em ${table}`;
  }

  if (sug.type === 'update') {
    const history = VERSIONED[table];
    const set = { ...data.set };
    if (history && Object.prototype.hasOwnProperty.call(set, 'content')) {
      const prev = await client.query(`SELECT content, version FROM ${t} WHERE id = $1 FOR UPDATE`, [data.id]);
      if (!prev.rows.length) throw new Error(`${table} id ${data.id} não existe.`);
      await client.query(
        `INSERT INTO ${quoteIdent(history)} (${quoteIdent(VERSIONED_FK[table])}, content, version, change_reason)
         VALUES ($1, $2, $3, $4)`,
        [data.id, prev.rows[0].content, prev.rows[0].version || 1, sug.reason]
      );
      set.version = (prev.rows[0].version || 1) + 1;
    }
    const cols = Object.keys(set);
    const res = await client.query(
      `UPDATE ${t} SET ${cols.map((c, i) => `${quoteIdent(c)} = $${i + 1}`).join(', ')}
       WHERE id = $${cols.length + 1}`,
      [...cols.map((c) => set[c]), data.id]
    );
    if (!res.rowCount) throw new Error(`${table} id ${data.id} não existe.`);
    return `atualizado id ${data.id} em ${table}`;
  }

  if (sug.type === 'delete') {
    const res = await client.query(`DELETE FROM ${t} WHERE id = $1`, [data.id]);
    if (!res.rowCount) throw new Error(`${table} id ${data.id} não existe.`);
    return `removido id ${data.id} de ${table}`;
  }

  throw new Error(`Tipo de sugestão desconhecido: ${sug.type}`);
}

async function resolveSuggestion(id, decision, user) {
  const client = await db.writer().connect();
  try {
    await client.query('BEGIN');
    const { rows } = await client.query(
      `SELECT * FROM pending_suggestions WHERE id = $1 AND status = 'pending' FOR UPDATE`, [id]
    );
    if (!rows.length) throw new Error(`Sugestão #${id} não existe ou já foi resolvida.`);
    let detail = 'sem alteração de dados';
    if (decision === 'approved') {
      detail = await applyApproved(client, rows[0]);
      await client.query('DELETE FROM query_cache');
    }
    await client.query(
      `UPDATE pending_suggestions SET status = $1, resolved_at = NOW(), resolved_by = $2 WHERE id = $3`,
      [decision, user.name, id]
    );
    await client.query('COMMIT');
    return `Sugestão #${id} ${decision === 'approved' ? 'aprovada' : 'rejeitada'} (${detail}).`;
  } catch (err) {
    await client.query('ROLLBACK').catch(() => {});
    throw err;
  } finally {
    client.release();
  }
}

const TOOLS = [
  {
    minRole: 'reader',
    definition: {
      name: 'brain_whoami',
      description: 'Mostra quem está conectado ao Segundo Cérebro e o que esse acesso permite.',
      inputSchema: { type: 'object', properties: {} },
    },
    async handler(_args, user) {
      const perms = {
        reader: 'consultar',
        suggester: 'consultar e sugerir conhecimento (fila de aprovação)',
        admin: 'consultar, sugerir e aprovar/rejeitar sugestões',
      };
      return text(`Conectado como ${user.name} (${user.role}): pode ${perms[user.role]}.`);
    },
  },
  {
    minRole: 'reader',
    definition: {
      name: 'brain_global_rules',
      description:
        'Carga inicial de sessão: regras GLOBAIS vigentes (scope_tags contém "global"). ' +
        'Chamar uma vez na abertura da sessão; para temas específicos use brain_search ou brain_scope_rules.',
      inputSchema: { type: 'object', properties: {} },
    },
    async handler() {
      const { rows } = await db.agent().query(
        `SELECT id, title, content FROM rules
         WHERE is_current AND 'global' = ANY(scope_tags) ORDER BY updated_at DESC`
      );
      return json(rows, '(nenhuma regra global vigente — o Brain ainda está vazio)');
    },
  },
  {
    minRole: 'reader',
    definition: {
      name: 'brain_scope_rules',
      description:
        'Regras vigentes de um ou mais domínios (scope_tags), puxadas sob demanda quando o domínio ' +
        'aparece na conversa. Ex.: ["financeiro"], ["mobile","ux"].',
      inputSchema: {
        type: 'object',
        properties: { scopes: { type: 'array', items: { type: 'string' }, description: 'Tags de escopo' } },
        required: ['scopes'],
      },
    },
    async handler(args) {
      if (!Array.isArray(args.scopes) || !args.scopes.length) {
        throw new Error('Parâmetro "scopes" é obrigatório (lista de textos).');
      }
      const { rows } = await db.agent().query(
        `SELECT id, title, content, scope_tags FROM rules
         WHERE is_current AND scope_tags && $1::text[] ORDER BY updated_at DESC`,
        [args.scopes.map(String)]
      );
      return json(rows, '(nenhuma regra vigente para esse escopo)');
    },
  },
  {
    minRole: 'reader',
    definition: {
      name: 'brain_search',
      description:
        'Busca por termo no Segundo Cérebro (texto em português). Tipos: rules, guidelines, errors, ' +
        'snippets, learnings (aprendizados de projeto) e artifacts (catálogo de skills/agentes). ' +
        'Retorna os resultados mais relevantes, nunca a tabela inteira.',
      inputSchema: {
        type: 'object',
        properties: {
          term: { type: 'string', description: 'Termo de busca' },
          type: { type: 'string', enum: Object.keys(SEARCH_TYPES), description: 'Tipo de conteúdo' },
          limit: { type: 'number', description: 'Máximo de resultados (padrão 8, máx. 50)' },
        },
        required: ['term', 'type'],
      },
    },
    async handler(args, user) {
      const term = requireString(args.term, 'term');
      const rows = await search(term, args.type, { limit: args.limit, user: user.name });
      return text(rows.length ? encodeToon(rows, args.type) : `(sem resultados para "${term}" em ${args.type})`);
    },
  },
  {
    minRole: 'reader',
    definition: {
      name: 'brain_list_projects',
      description: 'Lista os projetos cadastrados no Brain (id, nome, stack, descrição).',
      inputSchema: { type: 'object', properties: {} },
    },
    async handler() {
      const { rows } = await db.agent().query('SELECT id, name, stack, description FROM projects ORDER BY name');
      return json(rows, '(nenhum projeto cadastrado ainda)');
    },
  },
  {
    minRole: 'reader',
    definition: {
      name: 'brain_pending_suggestions',
      description:
        'Lista as sugestões aguardando aprovação. Administradores veem também os dados propostos. ' +
        'Nunca aprova nada.',
      inputSchema: { type: 'object', properties: {} },
    },
    async handler(_args, user) {
      const full = ROLE_LEVEL[user.role] >= ROLE_LEVEL.admin;
      const { rows } = await db.agent().query(
        `SELECT id, type, target_table, reason, suggested_by, requested_by, created_at
         ${full ? ', suggested_data' : ''}
         FROM pending_suggestions WHERE status = 'pending' ORDER BY created_at`
      );
      return json(rows, 'Nenhuma sugestão pendente.');
    },
  },
  {
    minRole: 'suggester',
    definition: {
      name: 'brain_suggest',
      description:
        'Enfileira conhecimento novo (regra, aprendizado, erro, snippet, projeto...) para aprovação. ' +
        'Nunca grava direto no Brain. Formatos de suggested_data: insert/new_category = { coluna: valor }; ' +
        'update = { id, set: { coluna: valor } }; delete = { id }. Regras levam scope_tags ' +
        '(ex.: ["global"] ou ["financeiro"]) combinadas com o usuário antes de sugerir.',
      inputSchema: {
        type: 'object',
        properties: {
          suggestions: {
            type: 'array',
            minItems: 1,
            maxItems: 20,
            items: {
              type: 'object',
              properties: {
                type: { type: 'string', enum: SUGGESTION_TYPES },
                target_table: { type: 'string', enum: KNOWLEDGE_TABLES },
                suggested_data: { type: 'object' },
                reason: { type: 'string', description: 'Por que esse conhecimento deve entrar no Brain' },
                suggested_by: { type: 'string', enum: AGENTS, description: 'Agente que sugere (padrão bibliotecario)' },
              },
              required: ['type', 'target_table', 'suggested_data', 'reason'],
            },
          },
        },
        required: ['suggestions'],
      },
    },
    async handler(args, user) {
      const items = Array.isArray(args.suggestions) ? args.suggestions : [];
      if (!items.length || items.length > 20) throw new Error('Envie de 1 a 20 sugestões.');
      items.forEach(validateSuggestion);
      const client = await db.agent().connect();
      try {
        await client.query('BEGIN');
        const ids = [];
        for (const s of items) {
          const { rows } = await client.query(
            `INSERT INTO pending_suggestions (type, target_table, suggested_data, reason, suggested_by, requested_by)
             VALUES ($1, $2, $3, $4, $5, $6) RETURNING id`,
            [s.type, s.target_table, JSON.stringify(s.suggested_data), s.reason.trim(),
              AGENTS.includes(s.suggested_by) ? s.suggested_by : 'bibliotecario', user.name]
          );
          ids.push(rows[0].id);
        }
        await client.query('COMMIT');
        return text(`${ids.length} sugestão(ões) na fila de aprovação: #${ids.join(', #')}.`);
      } catch (err) {
        await client.query('ROLLBACK').catch(() => {});
        throw err;
      } finally {
        client.release();
      }
    },
  },
  {
    minRole: 'suggester',
    definition: {
      name: 'brain_report_violation',
      description:
        'Registra uma violação de regra encontrada na fiscalização (Fiscal). Sinaliza, não bloqueia.',
      inputSchema: {
        type: 'object',
        properties: {
          rule_id: { type: 'number', description: 'Id da regra violada, se houver' },
          description: { type: 'string', description: 'O que foi violado' },
          artifact_reference: { type: 'string', description: 'Arquivo/entrega onde ocorreu' },
          session_context: { type: 'string', description: 'Contexto resumido da sessão' },
          resolution_plan: { type: 'string', description: 'Como corrigir' },
        },
        required: ['description'],
      },
    },
    async handler(args, user) {
      const ruleId = args.rule_id == null ? null : requireId(args.rule_id, 'rule_id');
      const { rows } = await db.agent().query(
        `INSERT INTO rule_violations (rule_id, artifact_reference, description, session_context, resolution_plan, reported_by)
         VALUES ($1, $2, $3, $4, $5, $6) RETURNING id`,
        [ruleId, args.artifact_reference || null, requireString(args.description, 'description'),
          args.session_context || null, args.resolution_plan || null, user.name]
      );
      return text(`Violação registrada (#${rows[0].id}).`);
    },
  },
  {
    minRole: 'admin',
    definition: {
      name: 'brain_approve',
      description:
        'Aprova e aplica uma sugestão pendente. SOMENTE por ordem explícita do usuário citando o id — ' +
        'nunca por iniciativa própria.',
      inputSchema: {
        type: 'object',
        properties: { id: { type: 'number', description: 'Id da sugestão' } },
        required: ['id'],
      },
    },
    async handler(args, user) {
      return text(await resolveSuggestion(requireId(args.id), 'approved', user));
    },
  },
  {
    minRole: 'admin',
    definition: {
      name: 'brain_reject',
      description: 'Rejeita uma sugestão pendente (não altera dados). SOMENTE por ordem explícita do usuário.',
      inputSchema: {
        type: 'object',
        properties: { id: { type: 'number', description: 'Id da sugestão' } },
        required: ['id'],
      },
    },
    async handler(args, user) {
      return text(await resolveSuggestion(requireId(args.id), 'rejected', user));
    },
  },
];

function toolsFor(user) {
  const level = ROLE_LEVEL[user.role] || 0;
  return TOOLS.filter((t) => level >= ROLE_LEVEL[t.minRole]);
}

module.exports = { toolsFor, validateSuggestion, ROLE_LEVEL };
