-- ============================================================
-- Migration 002: brain_agent may only INSERT suggestions
-- ============================================================
-- The MCP service role queues suggestions but must never change their
-- status or resolved_by; only the approval flow (brain_writer) does that,
-- so the approval trail cannot be forged by the service role.
-- ============================================================

BEGIN;

REVOKE UPDATE ON pending_suggestions FROM brain_agent;

COMMIT;
