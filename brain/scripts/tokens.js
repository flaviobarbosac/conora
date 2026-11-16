#!/usr/bin/env node
'use strict';

// Token admin for the shared Brain. Runs inside the brain-mcp container
// (writer credentials come from its environment). The token is printed once.
//
//   node scripts/tokens.js create --name "Leomar" --role suggester
//   node scripts/tokens.js list
//   node scripts/tokens.js revoke --id 3
//   node scripts/tokens.js role --id 3 --role admin

const db = require('../src/db');
const { hashToken, newToken } = require('../src/auth');

const ROLES = ['reader', 'suggester', 'admin'];

function flag(name) {
  const i = process.argv.indexOf(`--${name}`);
  return i >= 0 ? process.argv[i + 1] : undefined;
}

async function main() {
  const command = process.argv[2];
  const pool = db.writer();

  if (command === 'create') {
    const name = (flag('name') || '').trim();
    const role = flag('role') || 'suggester';
    if (!name) throw new Error('--name é obrigatório');
    if (!ROLES.includes(role)) throw new Error(`--role deve ser ${ROLES.join('/')}`);
    const token = newToken();
    const { rows } = await pool.query(
      `INSERT INTO brain_access_tokens (name, role, token_hash, token_prefix)
       VALUES ($1, $2, $3, $4) RETURNING id`,
      [name, role, hashToken(token), token.slice(0, 12)]
    );
    console.log(JSON.stringify({ id: rows[0].id, name, role, token }));
    return;
  }

  if (command === 'list') {
    const { rows } = await pool.query(
      `SELECT id, name, role, token_prefix, active, created_at, last_used_at, revoked_at
       FROM brain_access_tokens ORDER BY id`
    );
    console.table(rows);
    return;
  }

  if (command === 'revoke') {
    const id = Number(flag('id'));
    const res = await pool.query(
      'UPDATE brain_access_tokens SET active = FALSE, revoked_at = NOW() WHERE id = $1 AND active', [id]
    );
    console.log(res.rowCount ? `Token #${id} revogado (efeito em até 1 minuto).` : `Token #${id} não encontrado ou já revogado.`);
    return;
  }

  if (command === 'role') {
    const id = Number(flag('id'));
    const role = flag('role');
    if (!ROLES.includes(role)) throw new Error(`--role deve ser ${ROLES.join('/')}`);
    const res = await pool.query('UPDATE brain_access_tokens SET role = $1 WHERE id = $2', [role, id]);
    console.log(res.rowCount ? `Token #${id} agora é ${role} (efeito em até 1 minuto).` : `Token #${id} não encontrado.`);
    return;
  }

  throw new Error('Uso: tokens.js create --name <nome> --role reader|suggester|admin | list | revoke --id <n> | role --id <n> --role <papel>');
}

main()
  .catch((err) => {
    console.error('Erro:', err.message);
    process.exitCode = 1;
  })
  .finally(() => db.closeAll());
