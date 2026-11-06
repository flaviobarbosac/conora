\set ON_ERROR_STOP on
SET TIME ZONE 'America/Sao_Paulo';
BEGIN;
\ir seed-body.sql
COMMIT;
