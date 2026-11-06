-- Moves lançamentos onto the 90 days that start today (America/Sao_Paulo). Amounts stay exact.
WITH ranked AS (
  SELECT e."Id",
         ((row_number() OVER (ORDER BY e."OccurredAt", e."Id")) - 1) % 90 AS day_offset
  FROM entries e
  JOIN guard_user u ON e."UsuarioId" = u."Id"
  WHERE e."DeletedAt" IS NULL
)
UPDATE entries e
SET "OccurredAt" = (u.anchor + r.day_offset::int) AT TIME ZONE 'America/Sao_Paulo',
    "CompetenceYm" = to_char(u.anchor + r.day_offset::int, 'YYYY-MM'),
    "UpdatedAt" = NOW()
FROM ranked r
CROSS JOIN guard_user u
WHERE e."Id" = r."Id";

WITH ranked AS (
  SELECT b."Id",
         (row_number() OVER (ORDER BY b."CompetenceYm", b."Id")) - 1 AS month_index
  FROM budgets b
  JOIN guard_user u ON b."UsuarioId" = u."Id"
  WHERE b."DeletedAt" IS NULL
)
UPDATE budgets b
SET "CompetenceYm" = to_char(
      date_trunc('month', u.anchor)::date + make_interval(months => r.month_index::int),
      'YYYY-MM'),
    "UpdatedAt" = NOW()
FROM ranked r
CROSS JOIN guard_user u
WHERE b."Id" = r."Id";

WITH ranked AS (
  SELECT i."Id",
         (row_number() OVER (ORDER BY i."CompetenceYm", i."Id")) - 1 AS month_index
  FROM card_invoices i
  JOIN guard_user u ON i."UsuarioId" = u."Id"
  WHERE i."DeletedAt" IS NULL
)
UPDATE card_invoices i
SET "CompetenceYm" = to_char(
      date_trunc('month', u.anchor)::date + make_interval(months => r.month_index::int),
      'YYYY-MM'),
    "UpdatedAt" = NOW()
FROM ranked r
CROSS JOIN guard_user u
WHERE i."Id" = r."Id";

WITH ranked AS (
  SELECT p."Id",
         ((row_number() OVER (ORDER BY p."PurchasedAt", p."Id")) - 1) % 90 AS day_offset
  FROM card_purchases p
  JOIN guard_user u ON p."UsuarioId" = u."Id"
  WHERE p."DeletedAt" IS NULL
)
UPDATE card_purchases p
SET "PurchasedAt" = (u.anchor + r.day_offset::int) AT TIME ZONE 'America/Sao_Paulo',
    "CompetenceYm" = to_char(u.anchor + r.day_offset::int, 'YYYY-MM'),
    "FirstInvoiceYm" = CASE
      WHEN EXTRACT(DAY FROM (u.anchor + r.day_offset::int)) > c."ClosingDay"
        THEN to_char((date_trunc('month', u.anchor + r.day_offset::int) + interval '1 month'), 'YYYY-MM')
      ELSE to_char(u.anchor + r.day_offset::int, 'YYYY-MM')
    END,
    "UpdatedAt" = NOW()
FROM ranked r
JOIN card_purchases current_row ON current_row."Id" = r."Id"
JOIN credit_cards c ON c."Id" = current_row."CreditCardId"
CROSS JOIN guard_user u
WHERE p."Id" = r."Id";

-- A 90-day window can touch a fourth calendar month. Clone the latest budget into it.
INSERT INTO budgets (
  "Id", "UsuarioId", "CompetenceYm", "Mode", "Version", "CreatedAt", "UpdatedAt", "DeletedAt"
)
SELECT
  (
    substr(md5(src."Id"::text || missing.ym), 1, 8) || '-' ||
    substr(md5(src."Id"::text || missing.ym), 9, 4) || '-' ||
    substr(md5(src."Id"::text || missing.ym), 13, 4) || '-' ||
    substr(md5(src."Id"::text || missing.ym), 17, 4) || '-' ||
    substr(md5(src."Id"::text || missing.ym), 21, 12)
  )::uuid,
  src."UsuarioId",
  missing.ym,
  src."Mode",
  0,
  NOW(),
  NOW(),
  NULL
FROM (
  SELECT DISTINCT e."CompetenceYm" AS ym
  FROM entries e
  JOIN guard_user u ON e."UsuarioId" = u."Id"
  WHERE e."DeletedAt" IS NULL
    AND NOT EXISTS (
      SELECT 1
      FROM budgets b
      WHERE b."UsuarioId" = u."Id"
        AND b."DeletedAt" IS NULL
        AND b."CompetenceYm" = e."CompetenceYm"
    )
) missing
CROSS JOIN LATERAL (
  SELECT b.*
  FROM budgets b
  JOIN guard_user u ON b."UsuarioId" = u."Id"
  WHERE b."DeletedAt" IS NULL
  ORDER BY b."CompetenceYm" DESC
  LIMIT 1
) src;

INSERT INTO budget_lines (
  "Id", "UsuarioId", "BudgetId", "CategoryId", "PlannedAmount",
  "Version", "CreatedAt", "UpdatedAt", "DeletedAt", "ExpenseDay"
)
SELECT
  (
    substr(md5(bl."Id"::text || b."CompetenceYm"), 1, 8) || '-' ||
    substr(md5(bl."Id"::text || b."CompetenceYm"), 9, 4) || '-' ||
    substr(md5(bl."Id"::text || b."CompetenceYm"), 13, 4) || '-' ||
    substr(md5(bl."Id"::text || b."CompetenceYm"), 17, 4) || '-' ||
    substr(md5(bl."Id"::text || b."CompetenceYm"), 21, 12)
  )::uuid,
  bl."UsuarioId",
  b."Id",
  bl."CategoryId",
  bl."PlannedAmount",
  0,
  NOW(),
  NOW(),
  NULL,
  bl."ExpenseDay"
FROM budgets b
JOIN guard_user u ON b."UsuarioId" = u."Id"
JOIN budgets src
  ON src."UsuarioId" = u."Id"
 AND src."DeletedAt" IS NULL
 AND src."Id" <> b."Id"
JOIN budget_lines bl
  ON bl."BudgetId" = src."Id"
 AND bl."DeletedAt" IS NULL
WHERE b."DeletedAt" IS NULL
  AND b."CreatedAt" >= NOW() - interval '1 minute'
  AND NOT EXISTS (
    SELECT 1 FROM budget_lines existing
    WHERE existing."BudgetId" = b."Id"
  )
  AND src."CompetenceYm" = (
    SELECT max(other."CompetenceYm")
    FROM budgets other
    WHERE other."UsuarioId" = u."Id"
      AND other."DeletedAt" IS NULL
      AND other."Id" <> b."Id"
      AND EXISTS (
        SELECT 1 FROM budget_lines line
        WHERE line."BudgetId" = other."Id" AND line."DeletedAt" IS NULL
      )
  );

INSERT INTO audit_events (
  "Id", "UsuarioId", "EntityName", "EntityId", "Action", "Actor",
  "TimestampUtc", "CorrelationId", "DetailsJson", "Version", "CreatedAt", "UpdatedAt", "DeletedAt"
)
SELECT
  '0144a100-0000-7000-8000-000000000001'::uuid,
  u."Id",
  'guard-rail',
  u."Id"::text,
  'guard-rail-seed',
  'guard-rail',
  NOW(),
  'guard-rail-seed',
  jsonb_build_object('anchor', u.anchor, 'zone', 'America/Sao_Paulo', 'days', 90),
  0,
  NOW(),
  NOW(),
  NULL
FROM guard_user u;

DO $$
DECLARE
  uid uuid := (SELECT "Id" FROM guard_user);
  anchor date := (SELECT anchor FROM guard_user);
  days int;
  min_d date;
  max_d date;
  expense numeric(18,2);
  income numeric(18,2);
  planned numeric(18,2);
  entries int;
BEGIN
  SELECT count(DISTINCT ("OccurredAt" AT TIME ZONE 'America/Sao_Paulo')::date),
         min(("OccurredAt" AT TIME ZONE 'America/Sao_Paulo')::date),
         max(("OccurredAt" AT TIME ZONE 'America/Sao_Paulo')::date),
         coalesce(sum("Amount") FILTER (WHERE "Type" = 'Expense'), 0),
         coalesce(sum("Amount") FILTER (WHERE "Type" = 'Income'), 0),
         count(*)
    INTO days, min_d, max_d, expense, income, entries
  FROM entries
  WHERE "UsuarioId" = uid AND "DeletedAt" IS NULL;

  SELECT coalesce(sum("PlannedAmount"), 0)
    INTO planned
  FROM budget_lines
  WHERE "UsuarioId" = uid
    AND "DeletedAt" IS NULL
    AND "CreatedAt" < NOW() - interval '1 minute';

  IF entries <> 427 THEN
    RAISE EXCEPTION 'guard rail: % lançamentos, esperado 427', entries;
  END IF;
  IF days <> 90 OR min_d <> anchor OR max_d <> anchor + 89 THEN
    RAISE EXCEPTION 'guard rail: janela %..% (% dias), âncora %', min_d, max_d, days, anchor;
  END IF;
  IF expense <> 54744.00 OR income <> 56045.00 THEN
    RAISE EXCEPTION 'guard rail: totais % / %, esperado 54744.00 / 56045.00', expense, income;
  END IF;
  IF planned <> 12467581.23 THEN
    RAISE EXCEPTION 'guard rail: planejado %, esperado 12467581.23', planned;
  END IF;
  IF EXISTS (
    SELECT 1
    FROM entries e
    WHERE e."UsuarioId" = uid
      AND e."DeletedAt" IS NULL
      AND NOT EXISTS (
        SELECT 1
        FROM budgets b
        WHERE b."UsuarioId" = uid
          AND b."DeletedAt" IS NULL
          AND b."CompetenceYm" = e."CompetenceYm"
      )
  ) THEN
    RAISE EXCEPTION 'guard rail: lançamento sem orçamento na competência';
  END IF;

  RAISE NOTICE 'guard rail seed ok anchor=%', anchor;
END $$;
