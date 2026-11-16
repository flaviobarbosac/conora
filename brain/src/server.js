#!/usr/bin/env node
'use strict';

// Shared Segundo Cérebro — remote MCP server (Streamable HTTP, stateless).
//
// Auth: one token per person, sent either as
//   Authorization: Bearer <token>      (Claude Code: claude mcp add --header)
//   or in the path /mcp/<token>        (claude.ai / Claude Desktop custom connector URL)
// The database is never exposed: this service is the only way in, behind Caddy (HTTPS).

const http = require('http');
const { Server } = require('@modelcontextprotocol/sdk/server/index.js');
const { StreamableHTTPServerTransport } = require('@modelcontextprotocol/sdk/server/streamableHttp.js');
const { ListToolsRequestSchema, CallToolRequestSchema } = require('@modelcontextprotocol/sdk/types.js');

const db = require('./db');
const auth = require('./auth');
const { toolsFor } = require('./tools');
const instructions = require('./instructions');

const PORT = Number(process.env.PORT || 8080);
const MAX_BODY = 1024 * 1024;

function log(message, extra = {}) {
  // Never log tokens or request bodies.
  console.log(JSON.stringify({ ts: new Date().toISOString(), message, ...extra }));
}

function clientIp(req) {
  const fwd = String(req.headers['x-forwarded-for'] || '').split(',')[0].trim();
  return fwd || req.socket.remoteAddress || 'unknown';
}

function sendJson(res, status, body, headers = {}) {
  res.writeHead(status, { 'Content-Type': 'application/json', ...headers });
  res.end(JSON.stringify(body));
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    let size = 0;
    const chunks = [];
    req.on('data', (chunk) => {
      size += chunk.length;
      if (size > MAX_BODY) {
        reject(Object.assign(new Error('Payload too large'), { status: 413 }));
        req.destroy();
        return;
      }
      chunks.push(chunk);
    });
    req.on('end', () => {
      if (!chunks.length) return resolve(undefined);
      try {
        resolve(JSON.parse(Buffer.concat(chunks).toString('utf8')));
      } catch {
        reject(Object.assign(new Error('Invalid JSON'), { status: 400 }));
      }
    });
    req.on('error', reject);
  });
}

function extractToken(req, pathname) {
  const header = String(req.headers.authorization || '');
  if (/^Bearer\s+/i.test(header)) return header.replace(/^Bearer\s+/i, '').trim();
  const match = pathname.match(/^\/mcp\/([^/]+)\/?$/);
  return match ? decodeURIComponent(match[1]) : null;
}

function buildMcpServer(user) {
  const allowed = toolsFor(user);
  const byName = new Map(allowed.map((t) => [t.definition.name, t]));
  const server = new Server(
    { name: 'segundo-cerebro', version: '1.0.0' },
    { capabilities: { tools: {} }, instructions }
  );

  server.setRequestHandler(ListToolsRequestSchema, async () => ({
    tools: allowed.map((t) => t.definition),
  }));

  server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const { name, arguments: args = {} } = request.params;
    const tool = byName.get(name);
    if (!tool) {
      return { content: [{ type: 'text', text: `Ferramenta indisponível para este acesso: ${name}` }], isError: true };
    }
    const t0 = Date.now();
    try {
      const result = await tool.handler(args, user);
      log('tool', { user: user.name, tool: name, ms: Date.now() - t0 });
      return result;
    } catch (err) {
      log('tool_error', { user: user.name, tool: name, error: err.message });
      return { content: [{ type: 'text', text: `Erro: ${err.message}` }], isError: true };
    }
  });

  return server;
}

async function handleMcp(req, res, pathname) {
  const ip = clientIp(req);
  if (auth.isBlocked(ip)) {
    sendJson(res, 429, { error: 'too_many_attempts' }, { 'Retry-After': '600' });
    return;
  }

  const user = await auth.resolveUser(extractToken(req, pathname));
  if (!user) {
    auth.recordFailure(ip);
    log('auth_failed', { ip });
    sendJson(res, 401, { error: 'invalid_token' }, { 'WWW-Authenticate': 'Bearer' });
    return;
  }

  if (req.method !== 'POST') {
    // Stateless server: no server-initiated SSE stream and no sessions to delete.
    sendJson(res, 405, { jsonrpc: '2.0', error: { code: -32000, message: 'Method not allowed.' }, id: null },
      { Allow: 'POST' });
    return;
  }

  const body = await readBody(req);
  const server = buildMcpServer(user);
  const transport = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined, enableJsonResponse: true });
  res.on('close', () => {
    transport.close().catch(() => {});
    server.close().catch(() => {});
  });
  await server.connect(transport);
  await transport.handleRequest(req, res, body);
}

const httpServer = http.createServer(async (req, res) => {
  const { pathname } = new URL(req.url, 'http://localhost');
  try {
    if (pathname === '/health') {
      await db.agent().query('SELECT 1');
      sendJson(res, 200, { status: 'ok' });
      return;
    }
    if (pathname === '/mcp' || pathname.startsWith('/mcp/')) {
      await handleMcp(req, res, pathname);
      return;
    }
    sendJson(res, 404, { error: 'not_found' });
  } catch (err) {
    log('request_error', { path: pathname.startsWith('/mcp/') ? '/mcp/<token>' : pathname, error: err.message });
    if (!res.headersSent) sendJson(res, err.status || 500, { error: err.status ? err.message : 'internal_error' });
  }
});

httpServer.listen(PORT, () => log('listening', { port: PORT }));

function shutdown() {
  httpServer.close(() => db.closeAll().finally(() => process.exit(0)));
  setTimeout(() => process.exit(0), 5000).unref();
}
process.on('SIGTERM', shutdown);
process.on('SIGINT', shutdown);
