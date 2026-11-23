import http from 'node:http';
import { Pool } from 'pg';
import { hmac, isLicense, licenseLabel, newRefresh, normalizeLicense, randomLicense, safeEqual, sha256, signAccess, verifyAccess } from './crypto.mjs';
import { createOwnerHandler } from './owner.mjs';
import { createDiscordHandler } from './discord.mjs';

const required = ['DATABASE_URL', 'ZORA_KEY_PEPPER', 'ZORA_SESSION_SECRET'];
for (const key of required) if (!process.env[key] || process.env[key].length < 24) throw new Error(`Missing or insecure ${key}`);
const cfg = { pepper: process.env.ZORA_KEY_PEPPER, session: process.env.ZORA_SESSION_SECRET };
const db = new Pool({ connectionString: process.env.DATABASE_URL, max: 10, connectionTimeoutMillis: 8000, idleTimeoutMillis: 30000 });
const buckets = new Map();
const authRate = (ip) => {
  const now = Date.now();
  const old = buckets.get(ip);
  if (!old || old.until < now) { buckets.set(ip, { count: 1, until: now + 60_000 }); return true; }
  old.count += 1;
  return old.count <= 15;
};
setInterval(() => {
  const now = Date.now();
  for (const [ip, value] of buckets) if (value.until < now) buckets.delete(ip);
}, 60_000).unref();
const json = (res, status, payload) => {
  res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store', 'x-content-type-options': 'nosniff' });
  res.end(JSON.stringify(payload));
};
const error = (status, code, message) => Object.assign(new Error(message), { status, code });
async function body(req) {
  // Vercel's Node runtime may already have decoded application/json bodies.
  if (req.body !== undefined && req.body !== null) return req.body;
  let bytes = 0; const chunks = [];
  for await (const chunk of req) { bytes += chunk.length; if (bytes > 64000) throw error(413, 'too_large', 'Request body too large'); chunks.push(chunk); }
  try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); }
  catch { throw error(400, 'invalid_json', 'Expected JSON request'); }
}
function assertUsername(value) {
  if (typeof value !== 'string' || !/^[A-Za-z0-9_.-]{3,24}$/.test(value)) throw error(400, 'invalid_username', 'Username must be 3-24 letters, digits, _ . or -');
  return value.toLowerCase();
}
function assertInstallation(value) {
  if (typeof value !== 'string' || !/^[a-f0-9-]{36}$/i.test(value)) throw error(400, 'invalid_device', 'Invalid installation ID');
  return hmac(cfg.pepper, value.toLowerCase());
}
async function audit(client, id, action, actor, details = {}) {
  await client.query('INSERT INTO zora_audit (license_id,action,actor,details) VALUES ($1,$2,$3,$4)', [id, action, actor, JSON.stringify(details)]);
}
const isActive = l => l.status === 'active' && (!l.expires_at || new Date(l.expires_at).getTime() > Date.now());
const grant = (license, deviceHash, refreshToken) => ({
  username: license.username,
  accessToken: signAccess(cfg.session, { licenseId: license.id, deviceHash, username: license.username }),
  refreshToken,
  expiresIn: 900,
  status: license.status,
});
async function activate({ username, key, installationId }) {
  const normalized = normalizeLicense(key);
  if (!isLicense(normalized)) throw error(400, 'invalid_key', 'Invalid license key format');
  const user = assertUsername(username);
  const deviceHash = assertInstallation(installationId);
  const keyHash = hmac(cfg.pepper, normalized);
  const c = await db.connect();
  try {
    await c.query('BEGIN');
    const result = await c.query('SELECT * FROM zora_licenses WHERE key_hash=$1 FOR UPDATE', [keyHash]);
    const l = result.rows[0];
    if (!l) throw error(403, 'invalid_license', 'License key not recognized');
    if (!isActive(l)) throw error(403, 'license_disabled', 'License is banned, revoked, or expired');
    if (l.username && l.username !== user) throw error(403, 'username_mismatch', 'This license belongs to another username');
    const current = await c.query('SELECT id FROM zora_devices WHERE license_id=$1 AND device_hash=$2', [l.id, deviceHash]);
    if (!current.rowCount) {
      const count = await c.query('SELECT COUNT(*)::int AS count FROM zora_devices WHERE license_id=$1', [l.id]);
      if (count.rows[0].count >= l.max_devices) throw error(403, 'device_limit', 'Device limit reached; contact support');
    }
    const refreshToken = newRefresh();
    await c.query('UPDATE zora_licenses SET username=$2, activated_at=COALESCE(activated_at,now()), last_seen_at=now() WHERE id=$1', [l.id, user]);
    await c.query(`INSERT INTO zora_devices (license_id,device_hash,refresh_hash) VALUES ($1,$2,$3)
      ON CONFLICT (license_id,device_hash) DO UPDATE SET refresh_hash=EXCLUDED.refresh_hash,last_seen_at=now()`, [l.id, deviceHash, sha256(refreshToken)]);
    await audit(c, l.id, 'license.activated', user, { device: deviceHash.slice(0, 10) });
    await c.query('COMMIT');
    return grant({ ...l, username: user }, deviceHash, refreshToken);
  } catch (e) { await c.query('ROLLBACK'); throw e; }
  finally { c.release(); }
}
async function refresh({ username, installationId, refreshToken }) {
  const user = assertUsername(username);
  const deviceHash = assertInstallation(installationId);
  if (typeof refreshToken !== 'string' || refreshToken.length < 32 || refreshToken.length > 256) throw error(401, 'invalid_session', 'Please activate your license again');
  const c = await db.connect();
  try {
    await c.query('BEGIN');
    const result = await c.query(`SELECT l.*,d.id AS device_id,d.refresh_hash FROM zora_licenses l
      JOIN zora_devices d ON d.license_id=l.id WHERE l.username=$1 AND d.device_hash=$2 AND d.refresh_hash=$3 FOR UPDATE OF l,d`, [user, deviceHash, sha256(refreshToken)]);
    const l = result.rows[0];
    if (!l || !isActive(l) || !safeEqual(l.refresh_hash, sha256(refreshToken))) throw error(401, 'invalid_session', 'Please activate your license again');
    const nextToken = newRefresh();
    await c.query('UPDATE zora_devices SET refresh_hash=$2,last_seen_at=now() WHERE id=$1', [l.device_id, sha256(nextToken)]);
    await c.query('UPDATE zora_licenses SET last_seen_at=now() WHERE id=$1', [l.id]);
    await c.query('COMMIT');
    return grant(l, deviceHash, nextToken);
  } catch (e) { await c.query('ROLLBACK'); throw e; }
  finally { c.release(); }
}
async function verify(token) {
  const session = verifyAccess(cfg.session, token);
  if (!session) throw error(401, 'invalid_session', 'Session expired');
  const { rows } = await db.query(`SELECT l.status,l.expires_at,l.username FROM zora_licenses l JOIN zora_devices d ON d.license_id=l.id
    WHERE l.id=$1 AND d.device_hash=$2`, [session.licenseId, session.deviceHash]);
  if (!rows[0] || !isActive(rows[0]) || rows[0].username !== session.username) throw error(403, 'license_disabled', 'License is no longer active');
  return { active: true, username: session.username };
}
async function createLicenses({ quantity = 1, maxDevices = 1, note = '' }) {
  if (!Number.isInteger(quantity) || quantity < 1 || quantity > 500) throw error(400, 'invalid_quantity', 'Quantity must be between 1 and 500');
  if (!Number.isInteger(maxDevices) || maxDevices < 1 || maxDevices > 5) throw error(400, 'invalid_devices', 'Max devices must be 1-5');
  if (typeof note !== 'string' || note.length > 500) throw error(400, 'invalid_note', 'Note must be at most 500 characters');
  const c = await db.connect(); const out = [];
  try {
    await c.query('BEGIN');
    for (let i = 0; i < quantity; i++) {
      const key = randomLicense();
      const { rows } = await c.query('INSERT INTO zora_licenses(key_hash,key_label,max_devices,note) VALUES ($1,$2,$3,$4) RETURNING id,created_at', [hmac(cfg.pepper, key), licenseLabel(key), maxDevices, note]);
      await audit(c, rows[0].id, 'license.created', 'owner', { maxDevices });
      out.push({ key, id: rows[0].id, createdAt: rows[0].created_at });
    }
    await c.query('COMMIT'); return out;
  } catch (e) { await c.query('ROLLBACK'); throw e; }
  finally { c.release(); }
}
const handleOwner = createOwnerHandler({ db, json, error, body, audit, createLicenses });
const handleDiscord = createDiscordHandler({ db, json, error, audit, sessionSecret: cfg.session });
async function handler(req, res) {
  try {
    const url = new URL(req.url, 'http://localhost');
    const route = url.searchParams.get('route');
    const path = route !== null ? '/' + route.replace(/^\/+/, '') : url.pathname;
    if (req.method === 'GET' && path === '/health') {
      await db.query('SELECT 1'); return json(res, 200, { ok: true, name: 'Zora', priceUsd: 25 });
    }
    if (path.startsWith('/v1/license/') && !authRate(req.socket.remoteAddress ?? 'unknown')) throw error(429, 'rate_limited', 'Too many requests');
    if (req.method === 'POST' && path === '/v1/license/activate') return json(res, 200, await activate(await body(req)));
    if (req.method === 'POST' && path === '/v1/license/refresh') return json(res, 200, await refresh(await body(req)));
    if (req.method === 'POST' && path === '/v1/license/verify') {
      const token = (req.headers.authorization ?? '').replace(/^Bearer\s+/i, '');
      return json(res, 200, await verify(token));
    }
    if (path.startsWith('/v1/discord/')) return handleDiscord(req, res, path, url);
    if (path.startsWith('/v1/admin/')) return handleOwner(req, res, path, url);
    throw error(404, 'not_found', 'Endpoint not found');
  } catch (e) {
    if (!e.status) console.error('Zora API failure:', e.message);
    json(res, e.status || 500, { error: e.code || 'server_error', message: e.status ? e.message : 'Unexpected server error' });
  }
}
export default handler;
if (process.env.VERCEL !== '1' && process.env.ZORA_CLOUD_FUNCTION !== '1') {
  const host = process.env.HOST || '127.0.0.1';
  const port = Number(process.env.PORT || 8787);
  http.createServer(handler).listen(port, host, () => console.log(`Zora license API listening on ${host}:${port}`));
}
