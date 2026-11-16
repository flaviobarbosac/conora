#!/usr/bin/env bash
# Creates (idempotently) the isolated "brain" database on the shared Postgres
# instance and applies pending migrations from database/*.sql.
#
# Isolation model:
#   brain_owner  NOLOGIN  owns the brain database and every object in it
#   brain_agent  LOGIN    MCP service (read knowledge, write operational tables)
#   brain_writer LOGIN    approval flow and token admin
#   The brain roles get CONNECT only on "brain". CONNECT on the application
#   database is revoked from PUBLIC so the brain roles cannot even open it.
#
# Required env: PGHOST PGPORT MASTER_USER MASTER_PASSWORD APP_DB
#               AGENT_PASSWORD WRITER_PASSWORD SQL_DIR
# Optional env: BRAIN_DB (default brain), PSQL_IMAGE (default postgres:16-alpine),
#               PGSSLMODE (default require)
set -euo pipefail

: "${PGHOST:?}" "${PGPORT:?}" "${MASTER_USER:?}" "${MASTER_PASSWORD:?}" "${APP_DB:?}"
: "${AGENT_PASSWORD:?}" "${WRITER_PASSWORD:?}" "${SQL_DIR:?}"
BRAIN_DB="${BRAIN_DB:-brain}"
PSQL_IMAGE="${PSQL_IMAGE:-postgres:16-alpine}"
PGSSLMODE="${PGSSLMODE:-require}"

psql_run() {
  local db="$1"; shift
  docker run --rm -i --network host \
    -e PGHOST -e PGPORT -e PGUSER="$MASTER_USER" -e PGPASSWORD="$MASTER_PASSWORD" \
    -e PGDATABASE="$db" -e PGSSLMODE="$PGSSLMODE" -e PGAPPNAME=brain-bootstrap \
    -v "${SQL_DIR}:/sql:ro" \
    "$PSQL_IMAGE" psql -X -q -v ON_ERROR_STOP=1 "$@"
}

echo "== brain bootstrap: roles and database"
psql_run postgres \
  -v agent_pw="$AGENT_PASSWORD" -v writer_pw="$WRITER_PASSWORD" \
  -v master_user="$MASTER_USER" -v brain_db="$BRAIN_DB" <<'SQL'
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'brain_owner') THEN
    CREATE ROLE brain_owner NOLOGIN;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'brain_agent') THEN
    CREATE ROLE brain_agent LOGIN;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'brain_writer') THEN
    CREATE ROLE brain_writer LOGIN;
  END IF;
END $$;

-- New roles are already NOSUPERUSER/NOCREATEDB/NOCREATEROLE; restating it fails on
-- managed Postgres (RDS), where the master role is not a superuser.
ALTER ROLE brain_agent WITH LOGIN CONNECTION LIMIT 15 PASSWORD :'agent_pw';
ALTER ROLE brain_writer WITH LOGIN CONNECTION LIMIT 5 PASSWORD :'writer_pw';
ALTER ROLE brain_agent SET statement_timeout = '15s';
ALTER ROLE brain_writer SET statement_timeout = '30s';

-- The master role must be able to SET ROLE brain_owner to create objects owned by it.
GRANT brain_owner TO :"master_user" WITH SET TRUE;

SELECT format('CREATE DATABASE %I OWNER brain_owner', :'brain_db')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = :'brain_db') \gexec

SELECT format('REVOKE ALL ON DATABASE %I FROM PUBLIC', :'brain_db') \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO brain_agent, brain_writer', :'brain_db') \gexec
SQL

echo "== brain bootstrap: keep brain roles out of the application database"
# Only safe when every LOGIN role that relies on PUBLIC's CONNECT is a brain role
# or the database owner (the owner keeps CONNECT implicitly).
psql_run postgres -v app_db="$APP_DB" <<'SQL'
SELECT format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', :'app_db')
WHERE NOT EXISTS (
  SELECT 1 FROM pg_roles r
  WHERE r.rolcanlogin
    AND r.rolname NOT LIKE 'rds%'
    AND r.rolname NOT IN ('brain_agent', 'brain_writer')
    AND r.oid <> (SELECT datdba FROM pg_database WHERE datname = :'app_db')
    AND NOT r.rolsuper
) \gexec
SELECT CASE WHEN has_database_privilege('brain_agent', :'app_db', 'CONNECT')
  THEN 'WARN: brain_agent can still CONNECT to the application database'
  ELSE 'ok: brain roles cannot connect to the application database' END AS app_db_isolation;
SQL

echo "== brain bootstrap: migrations"
psql_run "$BRAIN_DB" <<'SQL'
SET ROLE brain_owner;
CREATE TABLE IF NOT EXISTS schema_migrations (
  version TEXT PRIMARY KEY,
  applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
SQL

for file in $(ls "$SQL_DIR"/*.sql | sort); do
  version="$(basename "$file")"
  applied="$(psql_run "$BRAIN_DB" -tA -v v="$version" <<'SQL'
SELECT count(*) FROM schema_migrations WHERE version = :'v';
SQL
)"
  if [ "$applied" = "1" ]; then
    echo "   skip  $version"
    continue
  fi
  echo "   apply $version"
  psql_run "$BRAIN_DB" -v v="$version" <<SQL
SET ROLE brain_owner;
\i /sql/${version}
INSERT INTO schema_migrations (version) VALUES (:'v');
SQL
done

echo "== brain bootstrap: done"
