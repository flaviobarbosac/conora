'use strict';

// Per-person bearer tokens. Only SHA-256 hashes live in the database.
// Lookups are cached briefly so each MCP call does not hit the database;
// a revoked token stops working within CACHE_MS.

const crypto = require('crypto');
const db = require('./db');

const CACHE_MS = 60 * 1000;
const TOUCH_MS = 5 * 60 * 1000;
const cache = new Map(); // hash -> { user, expires }
const lastTouch = new Map(); // id -> timestamp

const TOKEN_PATTERN = /^scb_[A-Za-z0-9_-]{32,64}$/;

function hashToken(token) {
  return crypto.createHash('sha256').update(token, 'utf8').digest('hex');
}

function newToken() {
  return 'scb_' + crypto.randomBytes(32).toString('base64url');
}

async function resolveUser(token) {
  if (!token || !TOKEN_PATTERN.test(token)) return null;
  const hash = hashToken(token);
  const now = Date.now();
  const cached = cache.get(hash);
  if (cached && cached.expires > now) return cached.user;

  const { rows } = await db.agent().query(
    'SELECT id, name, role FROM brain_access_tokens WHERE token_hash = $1 AND active',
    [hash]
  );
  const user = rows[0] || null;
  cache.set(hash, { user, expires: now + CACHE_MS });

  if (user && (now - (lastTouch.get(user.id) || 0)) > TOUCH_MS) {
    lastTouch.set(user.id, now);
    db.agent().query('UPDATE brain_access_tokens SET last_used_at = NOW() WHERE id = $1', [user.id])
      .catch(() => {});
  }
  return user;
}

// Brute-force guard: too many bad tokens from one address -> 429 for a while.
const FAIL_WINDOW_MS = 10 * 60 * 1000;
const FAIL_LIMIT = 20;
const failures = new Map(); // ip -> { count, since }

function isBlocked(ip) {
  const entry = failures.get(ip);
  if (!entry) return false;
  if (Date.now() - entry.since > FAIL_WINDOW_MS) {
    failures.delete(ip);
    return false;
  }
  return entry.count >= FAIL_LIMIT;
}

function recordFailure(ip) {
  const now = Date.now();
  if (failures.size > 10000) {
    // Bound memory under a spray of distinct addresses: drop expired entries first.
    for (const [key, entry] of failures) {
      if (now - entry.since > FAIL_WINDOW_MS) failures.delete(key);
    }
    if (failures.size > 10000) failures.clear();
  }
  const entry = failures.get(ip);
  if (!entry || now - entry.since > FAIL_WINDOW_MS) {
    failures.set(ip, { count: 1, since: now });
  } else {
    entry.count += 1;
  }
}

module.exports = { hashToken, newToken, resolveUser, isBlocked, recordFailure, TOKEN_PATTERN };
