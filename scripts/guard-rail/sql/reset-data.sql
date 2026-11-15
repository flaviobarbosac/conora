-- Deletes rows owned by the test user. Does not delete the login and does not touch global catalogs.
DELETE FROM import_preview_rows
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM import_batches
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM budget_lines
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM entries
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM card_purchases
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM card_invoices
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM budgets
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM month_locks
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM receipt_intakes
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM whatsapp_drafts
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM whatsapp_links
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM supplier_user_bank_accounts
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM supplier_user_categories
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM alert_seen
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM audit_events
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM refresh_tokens
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM lgpd_requests
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM user_feedbacks
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM user_milestones
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM life_projects
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM patrimony_items
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM income_sources
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM credit_cards
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM accounts
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM categories
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM family_owner_transfers
WHERE "FromUsuarioId" = (SELECT "Id" FROM guard_user)
   OR "ToUsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM family_invites
WHERE "InviterUsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM family_notices
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM family_group_members
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM family_groups
WHERE "BudgetOwnerUsuarioId" = (SELECT "Id" FROM guard_user)
   OR "CreatedByUsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM family_members
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM workspace_subscriptions
WHERE "UsuarioId" = (SELECT "Id" FROM guard_user);

DELETE FROM pre_registrations
WHERE lower("Email") = (SELECT email FROM guard_user);

DELETE FROM hotmart_course_purchases
WHERE lower("Email") = (SELECT email FROM guard_user);

DELETE FROM email_suppressions
WHERE lower("Email") = (SELECT email FROM guard_user);
