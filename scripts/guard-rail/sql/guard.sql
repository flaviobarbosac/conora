-- Localhost guard and the fixed test user. Refuses any other database or remote client.
DO $$
BEGIN
  IF current_database() <> 'conora' THEN
    RAISE EXCEPTION 'guard rail so aceita o banco local conora';
  END IF;

  IF inet_client_addr() IS NOT NULL
     AND host(inet_client_addr()) NOT IN ('127.0.0.1', '::1') THEN
    RAISE EXCEPTION 'guard rail so aceita localhost';
  END IF;
END $$;

CREATE TEMP TABLE guard_user ON COMMIT DROP AS
SELECT
  "Id",
  lower("Email") AS email,
  (CURRENT_TIMESTAMP AT TIME ZONE 'America/Sao_Paulo')::date AS anchor
FROM users
WHERE lower("Email") = 'flavio.barbosa.vix@gmail.com'
  AND "DeletedAt" IS NULL;

DO $$
BEGIN
  IF (SELECT count(*) FROM guard_user) <> 1 THEN
    RAISE EXCEPTION 'usuario flavio.barbosa.vix@gmail.com nao encontrado no localhost';
  END IF;
END $$;
