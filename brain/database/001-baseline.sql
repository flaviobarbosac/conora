-- ============================================================
-- Segundo Cérebro (shared instance) — Migration 001: baseline
-- ============================================================
-- Fresh, empty knowledge base. Same table model as the original Brain
-- (schema + lexical indexes + query cache + telemetry + rule scopes), plus:
--   * brain_access_tokens  — one hashed token per person (remote MCP auth)
--   * requested_by / resolved_by on pending_suggestions (who asked, who decided)
--   * usuario on brain_telemetry
-- No knowledge rows are seeded except the founding categories.
--
-- Applied by deploy/bootstrap-db.sh as role brain_owner (owner of every object).
-- Idempotent: safe to re-run.
-- Table classes:
--   KNOWLEDGE  : written only by the approval flow (brain_writer)
--   OPERATIONAL: the MCP service role may INSERT/UPDATE (brain_agent)
-- ============================================================

BEGIN;

CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- ------------------------------------------------------------
-- KNOWLEDGE TABLES
-- ------------------------------------------------------------

CREATE TABLE IF NOT EXISTS brain_categories (
  id SERIAL PRIMARY KEY,
  name VARCHAR(100) UNIQUE NOT NULL,
  description TEXT NOT NULL,
  creation_reason TEXT NOT NULL,
  created_at TIMESTAMP DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS rules (
  id SERIAL PRIMARY KEY,
  category_id INT REFERENCES brain_categories(id),
  title VARCHAR(200) NOT NULL,
  content TEXT NOT NULL,
  scope_tags TEXT[] NOT NULL DEFAULT '{global}',
  version INT DEFAULT 1,
  is_current BOOLEAN DEFAULT TRUE,
  created_at TIMESTAMP DEFAULT NOW(),
  updated_at TIMESTAMP DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS rules_history (
  id SERIAL PRIMARY KEY,
  rule_id INT REFERENCES rules(id),
  content TEXT NOT NULL,
  version INT NOT NULL,
  changed_at TIMESTAMP DEFAULT NOW(),
  change_reason TEXT
);

CREATE TABLE IF NOT EXISTS guidelines (
  id SERIAL PRIMARY KEY,
  category_id INT REFERENCES brain_categories(id),
  title VARCHAR(200) NOT NULL,
  content TEXT NOT NULL,
  version INT DEFAULT 1,
  is_current BOOLEAN DEFAULT TRUE,
  created_at TIMESTAMP DEFAULT NOW(),
  updated_at TIMESTAMP DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS guidelines_history (
  id SERIAL PRIMARY KEY,
  guideline_id INT REFERENCES guidelines(id),
  content TEXT NOT NULL,
  version INT NOT NULL,
  changed_at TIMESTAMP DEFAULT NOW(),
  change_reason TEXT
);

CREATE TABLE IF NOT EXISTS projects (
  id SERIAL PRIMARY KEY,
  name VARCHAR(200) NOT NULL,
  stack TEXT,
  description TEXT,
  created_at TIMESTAMP DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS project_learnings (
  id SERIAL PRIMARY KEY,
  project_id INT REFERENCES projects(id),
  type VARCHAR(20) CHECK (type IN ('worked', 'failed', 'observation')),
  content TEXT NOT NULL,
  tags TEXT[],
  created_at TIMESTAMP DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS error_patterns (
  id SERIAL PRIMARY KEY,
  category_id INT REFERENCES brain_categories(id),
  title VARCHAR(200) NOT NULL,
  description TEXT NOT NULL,
  solution TEXT,
  occurrence_count INT DEFAULT 1,
  last_seen TIMESTAMP DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS artifacts (
  id SERIAL PRIMARY KEY,
  skill_name VARCHAR(100) NOT NULL,
  file_path TEXT NOT NULL,
  description TEXT,
  last_verified TIMESTAMP DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS snippets (
  id SERIAL PRIMARY KEY,
  category_id INT REFERENCES brain_categories(id),
  title VARCHAR(200) NOT NULL,
  language VARCHAR(50),
  content TEXT NOT NULL,
  context TEXT,
  tags TEXT[],
  created_at TIMESTAMP DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS directory_structures (
  id SERIAL PRIMARY KEY,
  project_id INT REFERENCES projects(id),
  stack VARCHAR(100),
  structure TEXT NOT NULL,
  notes TEXT,
  created_at TIMESTAMP DEFAULT NOW()
);

-- ------------------------------------------------------------
-- OPERATIONAL TABLES
-- ------------------------------------------------------------

CREATE TABLE IF NOT EXISTS rule_violations (
  id SERIAL PRIMARY KEY,
  rule_id INT REFERENCES rules(id),
  artifact_reference TEXT,
  description TEXT NOT NULL,
  session_context TEXT,
  occurrence_count INT DEFAULT 1,
  resolution_plan TEXT,
  reported_by VARCHAR(100),
  created_at TIMESTAMP DEFAULT NOW(),
  last_seen TIMESTAMP DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS pending_suggestions (
  id SERIAL PRIMARY KEY,
  type VARCHAR(50) NOT NULL,           -- insert | update | delete | new_category
  target_table VARCHAR(100) NOT NULL,
  suggested_data JSONB NOT NULL,
  reason TEXT NOT NULL,
  suggested_by VARCHAR(50) NOT NULL,   -- agent: bibliotecario | fiscal | orquestrador
  requested_by VARCHAR(100),           -- person whose token created it
  status VARCHAR(20) DEFAULT 'pending' CHECK (status IN ('pending', 'approved', 'rejected')),
  created_at TIMESTAMP DEFAULT NOW(),
  resolved_at TIMESTAMP,
  resolved_by VARCHAR(100)             -- person who approved/rejected
);

CREATE TABLE IF NOT EXISTS query_cache (
  id              SERIAL PRIMARY KEY,
  tipo            VARCHAR(30)  NOT NULL,
  termo           TEXT         NOT NULL,
  termo_hash      CHAR(64)     NOT NULL,
  escopo_projeto  VARCHAR(120),
  versao_regras   INTEGER      NOT NULL DEFAULT 0,
  resposta        JSONB        NOT NULL,
  ttl_expires_at  TIMESTAMP,
  hit_count       INTEGER      NOT NULL DEFAULT 0,
  created_at      TIMESTAMP    DEFAULT NOW(),
  last_hit_at     TIMESTAMP
);

CREATE TABLE IF NOT EXISTS brain_telemetry (
  id            SERIAL PRIMARY KEY,
  ts            TIMESTAMP DEFAULT NOW(),
  event         VARCHAR(40)  NOT NULL,
  tipo          VARCHAR(30),
  termo         TEXT,
  usuario       VARCHAR(100),
  cache_source  VARCHAR(16),
  result_count  INTEGER,
  duration_ms   INTEGER
);

-- One row per person. Only the SHA-256 of the token is stored; the token
-- itself is shown once, at creation (scripts/tokens.js).
CREATE TABLE IF NOT EXISTS brain_access_tokens (
  id            SERIAL PRIMARY KEY,
  name          VARCHAR(100) NOT NULL,
  role          VARCHAR(20)  NOT NULL CHECK (role IN ('reader', 'suggester', 'admin')),
  token_hash    CHAR(64)     NOT NULL UNIQUE,
  token_prefix  VARCHAR(12)  NOT NULL,
  active        BOOLEAN      NOT NULL DEFAULT TRUE,
  created_at    TIMESTAMP    DEFAULT NOW(),
  last_used_at  TIMESTAMP,
  revoked_at    TIMESTAMP
);

-- ------------------------------------------------------------
-- INDEXES
-- ------------------------------------------------------------

CREATE INDEX IF NOT EXISTS idx_rules_category ON rules(category_id) WHERE is_current;
CREATE INDEX IF NOT EXISTS idx_guidelines_category ON guidelines(category_id) WHERE is_current;
CREATE INDEX IF NOT EXISTS idx_learnings_project ON project_learnings(project_id);
CREATE INDEX IF NOT EXISTS idx_suggestions_status ON pending_suggestions(status) WHERE status = 'pending';
CREATE INDEX IF NOT EXISTS idx_rules_scope_tags ON rules USING GIN (scope_tags);

CREATE INDEX IF NOT EXISTS idx_learnings_tags ON project_learnings USING GIN(tags);
CREATE INDEX IF NOT EXISTS idx_snippets_tags ON snippets USING GIN(tags);

-- FTS expressions MUST match src/search.js so the indexes are used.
CREATE INDEX IF NOT EXISTS idx_rules_fts ON rules
  USING GIN (to_tsvector('portuguese', coalesce(title,'') || ' ' || coalesce(content,'')));
CREATE INDEX IF NOT EXISTS idx_guidelines_fts ON guidelines
  USING GIN (to_tsvector('portuguese', coalesce(title,'') || ' ' || coalesce(content,'')));
CREATE INDEX IF NOT EXISTS idx_error_patterns_fts ON error_patterns
  USING GIN (to_tsvector('portuguese',
    coalesce(title,'') || ' ' || coalesce(description,'') || ' ' || coalesce(solution,'')));
CREATE INDEX IF NOT EXISTS idx_snippets_fts ON snippets
  USING GIN (to_tsvector('portuguese',
    coalesce(title,'') || ' ' || coalesce(content,'') || ' ' || coalesce(context,'')));
CREATE INDEX IF NOT EXISTS idx_learnings_fts ON project_learnings
  USING GIN (to_tsvector('portuguese', coalesce(content,'')));
CREATE INDEX IF NOT EXISTS idx_artifacts_fts ON artifacts
  USING GIN (to_tsvector('portuguese', coalesce(skill_name,'') || ' ' || coalesce(description,'')));

CREATE INDEX IF NOT EXISTS idx_artifacts_skill_name_trgm ON artifacts USING GIN (skill_name gin_trgm_ops);
CREATE INDEX IF NOT EXISTS idx_directory_structures_stack_trgm ON directory_structures USING GIN (stack gin_trgm_ops);

CREATE UNIQUE INDEX IF NOT EXISTS idx_query_cache_hash ON query_cache (termo_hash);
CREATE INDEX IF NOT EXISTS idx_query_cache_tipo ON query_cache (tipo);
CREATE INDEX IF NOT EXISTS idx_brain_telemetry_ts ON brain_telemetry (ts);
CREATE INDEX IF NOT EXISTS idx_brain_telemetry_event ON brain_telemetry (event);

-- ------------------------------------------------------------
-- TRIGGERS
-- ------------------------------------------------------------

CREATE OR REPLACE FUNCTION set_updated_at() RETURNS trigger AS $$
BEGIN
  NEW.updated_at = NOW();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_rules_updated_at ON rules;
CREATE TRIGGER trg_rules_updated_at BEFORE UPDATE ON rules
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();

DROP TRIGGER IF EXISTS trg_guidelines_updated_at ON guidelines;
CREATE TRIGGER trg_guidelines_updated_at BEFORE UPDATE ON guidelines
  FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- ------------------------------------------------------------
-- GRANTS (least privilege)
-- ------------------------------------------------------------

GRANT USAGE ON SCHEMA public TO brain_agent, brain_writer;

-- brain_agent (MCP service): read knowledge, write operational tables only.
GRANT SELECT ON brain_categories, rules, rules_history, guidelines, guidelines_history,
  projects, project_learnings, error_patterns, artifacts, snippets, directory_structures,
  rule_violations, pending_suggestions, query_cache, brain_telemetry TO brain_agent;
GRANT INSERT, UPDATE ON pending_suggestions, rule_violations TO brain_agent;
GRANT INSERT, UPDATE ON query_cache TO brain_agent;
GRANT INSERT ON brain_telemetry TO brain_agent;
GRANT SELECT (id, name, role, token_hash, active) ON brain_access_tokens TO brain_agent;
GRANT UPDATE (last_used_at) ON brain_access_tokens TO brain_agent;
GRANT SELECT, USAGE ON SEQUENCE pending_suggestions_id_seq, rule_violations_id_seq,
  query_cache_id_seq, brain_telemetry_id_seq TO brain_agent;

-- brain_writer (approval flow + token admin): full write.
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO brain_writer;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO brain_writer;

-- ------------------------------------------------------------
-- FOUNDING CATEGORIES (structure, not project knowledge)
-- ------------------------------------------------------------

INSERT INTO brain_categories (name, description, creation_reason) VALUES
('rules',       'Regras operacionais que a IA deve seguir',          'Categoria fundadora'),
('guidelines',  'Orientações de boas práticas por domínio',          'Categoria fundadora'),
('projects',    'Contexto e aprendizados de projetos',               'Categoria fundadora'),
('errors',      'Padrões de erros recorrentes e soluções',           'Categoria fundadora'),
('snippets',    'Trechos de código reutilizáveis',                   'Categoria fundadora'),
('directories', 'Estruturas de diretório por stack/projeto',         'Categoria fundadora'),
('artifacts',   'Catálogo de skills e agentes',                      'Categoria fundadora')
ON CONFLICT (name) DO NOTHING;

COMMIT;
