'use strict';

// Two pools, two privilege levels:
//   agent  -> brain_agent  (reads knowledge, writes operational tables only)
//   writer -> brain_writer (approval flow and token admin)
// Database grants enforce the split; the application never relies on convention alone.

const { Pool } = require('pg');

function required(name) {
  const value = process.env[name];
  if (!value) throw new Error(`Missing environment variable ${name}`);
  return value;
}

function poolFor(userVar, passwordVar, max) {
  const ssl = String(process.env.BRAIN_DB_SSL || 'require').toLowerCase();
  return new Pool({
    host: required('BRAIN_DB_HOST'),
    port: Number(process.env.BRAIN_DB_PORT || 5432),
    database: required('BRAIN_DB_NAME'),
    user: required(userVar),
    password: required(passwordVar),
    ssl: ssl === 'disable' ? false : { rejectUnauthorized: false },
    max,
    idleTimeoutMillis: 30000,
    connectionTimeoutMillis: 10000,
    application_name: 'brain-mcp',
  });
}

let agentPool = null;
let writerPool = null;

function agent() {
  if (!agentPool) agentPool = poolFor('BRAIN_DB_AGENT_USER', 'BRAIN_DB_AGENT_PASSWORD', 5);
  return agentPool;
}

function writer() {
  if (!writerPool) writerPool = poolFor('BRAIN_DB_WRITER_USER', 'BRAIN_DB_WRITER_PASSWORD', 2);
  return writerPool;
}

async function closeAll() {
  await Promise.all([agentPool && agentPool.end(), writerPool && writerPool.end()]);
}

module.exports = { agent, writer, closeAll };
