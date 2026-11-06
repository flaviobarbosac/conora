\set ON_ERROR_STOP on
SET TIME ZONE 'America/Sao_Paulo';
BEGIN;
\ir guard.sql
\ir reset-data.sql
DO $$
BEGIN
  RAISE NOTICE 'guard rail reset ok email=%', (SELECT email FROM guard_user);
END $$;
COMMIT;
