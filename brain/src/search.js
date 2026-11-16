'use strict';

// Lexical (Portuguese full-text) retrieval with an exact-match query cache and
// telemetry. Table and column names are a fixed allowlist, never user input.

const crypto = require('crypto');
const db = require('./db');

const SEARCH_TYPES = {
  rules: {
    table: 'rules', filter: 'is_current',
    fts: "coalesce(title,'') || ' ' || coalesce(content,'')",
    select: ['id', 'title', 'content', 'scope_tags'],
  },
  guidelines: {
    table: 'guidelines', filter: 'is_current',
    fts: "coalesce(title,'') || ' ' || coalesce(content,'')",
    select: ['id', 'category_id', 'title', 'content'],
  },
  errors: {
    table: 'error_patterns',
    fts: "coalesce(title,'') || ' ' || coalesce(description,'') || ' ' || coalesce(solution,'')",
    select: ['id', 'title', 'description', 'solution', 'occurrence_count'],
  },
  snippets: {
    table: 'snippets',
    fts: "coalesce(title,'') || ' ' || coalesce(content,'') || ' ' || coalesce(context,'')",
    select: ['id', 'title', 'language', 'content', 'context', 'tags'],
  },
  learnings: {
    table: 'project_learnings',
    fts: "coalesce(content,'')",
    select: ['id', 'project_id', 'type', 'content', 'tags'],
  },
  artifacts: {
    table: 'artifacts',
    fts: "coalesce(skill_name,'') || ' ' || coalesce(description,'')",
    select: ['id', 'skill_name', 'file_path', 'description'],
  },
};

function quoteIdent(name) {
  if (!/^[a-z_][a-z0-9_]*$/.test(name)) throw new Error(`Identificador inválido: ${name}`);
  return `"${name}"`;
}

function buildSql(cfg, limit) {
  const tsv = `to_tsvector('portuguese', ${cfg.fts})`;
  const filter = cfg.filter || 'TRUE';
  const cols = cfg.select.map((c) => `m.${quoteIdent(c)}`).join(', ');
  return `
    WITH lex AS (
      SELECT id, ts_rank(${tsv}, plainto_tsquery('portuguese', $1)) AS rnk
      FROM ${cfg.table}
      WHERE ${filter} AND ${tsv} @@ plainto_tsquery('portuguese', $1)
    )
    SELECT ${cols}, round(lex.rnk::numeric, 6) AS score
    FROM ${cfg.table} m
    JOIN lex ON lex.id = m.id
    ORDER BY score DESC, m.id
    LIMIT ${limit}`;
}

async function search(term, type, { limit, user } = {}) {
  const cfg = SEARCH_TYPES[type];
  if (!cfg) throw new Error(`Tipo inválido: ${type}. Use um de: ${Object.keys(SEARCH_TYPES).join(', ')}`);
  const max = Math.min(Math.max(Number(limit) || 8, 1), 50);

  const norm = String(term).trim().toLowerCase().replace(/\s+/g, ' ');
  const hash = crypto.createHash('sha256').update(`${type}|${norm}|${max}`).digest('hex');
  const t0 = Date.now();
  const pool = db.agent();

  let rows;
  let source = 'cache-L1';
  const hit = await pool.query(
    `UPDATE query_cache SET hit_count = hit_count + 1, last_hit_at = NOW()
     WHERE termo_hash = $1 AND (ttl_expires_at IS NULL OR ttl_expires_at > NOW())
     RETURNING resposta`, [hash]
  );
  if (hit.rows.length) {
    rows = hit.rows[0].resposta;
  } else {
    source = 'fresh';
    rows = (await pool.query(buildSql(cfg, max), [term])).rows;
    await pool.query(
      `INSERT INTO query_cache (tipo, termo, termo_hash, resposta)
       VALUES ($1, $2, $3, $4)
       ON CONFLICT (termo_hash) DO UPDATE SET resposta = EXCLUDED.resposta`,
      [type, term, hash, JSON.stringify(rows)]
    );
  }

  await pool.query(
    `INSERT INTO brain_telemetry (event, tipo, termo, usuario, cache_source, result_count, duration_ms)
     VALUES ('search', $1, $2, $3, $4, $5, $6)`,
    [type, term, user || null, source, rows.length, Date.now() - t0]
  ).catch(() => {});

  return rows;
}

module.exports = { SEARCH_TYPES, quoteIdent, search };
