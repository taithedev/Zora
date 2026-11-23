import { createHash, randomBytes } from 'node:crypto';
import { verifyAccess } from './crypto.mjs';

const DEFAULT_AUTH = 'https://ep-rough-recipe-bcjp6wq7.neonauth.c-16.us-east-1.aws.neon.tech/neondb/auth';
const DEFAULT_CLIENT = 'NKTcizNpNAplTPMSxWfSjVBIrxLHuApK';
const DEFAULT_REDIRECT = 'https://br-frosty-bar-bbmdnhr7-zoralicense.compute.c-15.us-east-1.aws.neon.tech/v1/zuzify/callback';
const RESOURCE = 'https://zuzify.xyz';
const hash = value => createHash('sha256').update(value).digest('hex');
const validUuid = value => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
const validState = value => typeof value === 'string' && /^[A-Za-z0-9_-]{40,128}$/.test(value);

function finishPage(res, success) {
  const title = success ? 'Zuzify connected' : 'Unable to connect Zuzify';
  const detail = success
    ? 'Your Zora license is now linked to your Zuzify ID. Return to Zora.'
    : 'This request expired, was denied, or could not be completed. Return to Zora and try again.';
  res.writeHead(success ? 200 : 400, {
    'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store',
    'referrer-policy': 'no-referrer', 'x-frame-options': 'DENY',
    'content-security-policy': "default-src 'none'; base-uri 'none'; frame-ancestors 'none'"
  });
  res.end(`<!doctype html><html lang="en"><meta charset="utf-8"><title>${title}</title><body style="font:16px system-ui;color:#fafafa;background:#050505;display:grid;place-items:center;min-height:100vh"><main style="max-width:520px;padding:32px"><h1>${title}</h1><p>${detail}</p></main></body></html>`);
}

/**
 * OAuth 2.1 public-client authorization-code + PKCE.
 * The Zora access token stays in the desktop app; the Zuzify code verifier and
 * authorization code are kept server-side and never passed to the desktop.
 * No Zuzify password, OAuth token or license key is persisted.
 */
export function createZuzifyHandler({ db, json, error, audit, sessionSecret, fetcher = fetch, env = process.env }) {
  const authUrl = (env.ZUZIFY_AUTH_URL || DEFAULT_AUTH).replace(/\/$/, '');
  const clientId = env.ZUZIFY_OAUTH_CLIENT_ID || DEFAULT_CLIENT;
  const redirectUri = env.ZUZIFY_REDIRECT_URI || DEFAULT_REDIRECT;
  const ready = authUrl.startsWith('https://') && redirectUri.startsWith('https://') && !!clientId;

  async function sessionFrom(req) {
    const token = String(req.headers.authorization || '').replace(/^Bearer\s+/i, '');
    const payload = verifyAccess(sessionSecret, token);
    if (!payload?.licenseId || !payload?.deviceHash || !payload?.username) {
      throw error(401, 'invalid_session', 'Sign in to Zora again');
    }
    const { rows } = await db.query(`SELECT l.id,l.username FROM zora_licenses l
      JOIN zora_devices d ON d.license_id=l.id
      WHERE l.id=$1 AND d.device_hash=$2 AND l.status='active'
      AND (l.expires_at IS NULL OR l.expires_at>now())`, [payload.licenseId, payload.deviceHash]);
    if (!rows[0] || rows[0].username !== payload.username) {
      throw error(401, 'invalid_session', 'Zora license session is no longer active');
    }
    return rows[0];
  }

  async function me(req, res) {
    const license = await sessionFrom(req);
    const { rows } = await db.query('SELECT zuzify_user_id,linked_at FROM zora_zuzify_links WHERE license_id=$1', [license.id]);
    return json(res, 200, {
      linked: Boolean(rows[0]),
      zuzify: rows[0] ? { userId: rows[0].zuzify_user_id, linkedAt: rows[0].linked_at } : null
    });
  }

  async function start(req, res) {
    const license = await sessionFrom(req);
    if (!ready) throw error(503, 'zuzify_unavailable', 'Zuzify OAuth is not configured');
    const { rows } = await db.query('SELECT 1 FROM zora_zuzify_links WHERE license_id=$1', [license.id]);
    if (rows.length) throw error(409, 'already_linked', 'Disconnect the current Zuzify account before connecting a different one');
    const state = randomBytes(32).toString('base64url');
    const verifier = randomBytes(48).toString('base64url');
    const challenge = createHash('sha256').update(verifier).digest('base64url');
    await db.query('DELETE FROM zora_zuzify_link_requests WHERE license_id=$1 OR expires_at<now()', [license.id]);
    await db.query(`INSERT INTO zora_zuzify_link_requests(state_hash,license_id,code_verifier,expires_at)
      VALUES($1,$2,$3,now()+interval '10 minutes')`, [hash(state), license.id, verifier]);
    const target = new URL(authUrl + '/oauth2/authorize');
    for (const [key,value] of Object.entries({
      response_type: 'code', client_id: clientId, redirect_uri: redirectUri,
      scope: 'openid profile', state, code_challenge: challenge,
      code_challenge_method: 'S256', resource: RESOURCE
    })) target.searchParams.set(key, value);
    return json(res, 200, { url: target.toString(), expiresIn: 600 });
  }

  async function callback(url, res) {
    const state = url.searchParams.get('state');
    const code = url.searchParams.get('code');
    if (!ready || !validState(state) || !code || code.length > 2048 || url.searchParams.has('error')) {
      return finishPage(res, false);
    }
    const claimed = await db.query(`UPDATE zora_zuzify_link_requests SET consumed_at=now()
      WHERE state_hash=$1 AND consumed_at IS NULL AND expires_at>now()
      RETURNING license_id,code_verifier`, [hash(state)]);
    const request = claimed.rows[0];
    if (!request) return finishPage(res, false);
    try {
      const tokenResponse = await fetcher(authUrl + '/oauth2/token', {
        method: 'POST', headers: { 'content-type': 'application/x-www-form-urlencoded' },
        body: new URLSearchParams({
          grant_type: 'authorization_code', client_id: clientId, code,
          code_verifier: request.code_verifier, redirect_uri: redirectUri,
          resource: RESOURCE
        }), signal: AbortSignal.timeout(10000)
      });
      if (!tokenResponse.ok) return finishPage(res, false);
      const tokens = await tokenResponse.json();
      if (typeof tokens.access_token !== 'string' || !tokens.access_token) return finishPage(res, false);
      const identityResponse = await fetcher(authUrl + '/oauth2/userinfo', {
        headers: { authorization: 'Bearer ' + tokens.access_token },
        signal: AbortSignal.timeout(10000)
      });
      if (!identityResponse.ok) return finishPage(res, false);
      const identity = await identityResponse.json();
      if (!validUuid(identity.sub)) return finishPage(res, false);

      const client = await db.connect();
      try {
        await client.query('BEGIN');
        const verified = await client.query(`SELECT id,username FROM zora_licenses WHERE id=$1
          AND status='active' AND (expires_at IS NULL OR expires_at>now()) FOR UPDATE`, [request.license_id]);
        if (!verified.rows[0]) throw new Error('License is unavailable');
        await client.query(`INSERT INTO zora_zuzify_links(license_id,zuzify_user_id)
          VALUES($1,$2)`, [request.license_id, identity.sub]);
        await audit(client, request.license_id, 'zuzify.linked', verified.rows[0].username, {});
        await client.query('COMMIT');
      } catch (e) { await client.query('ROLLBACK'); throw e; }
      finally { client.release(); }
      return finishPage(res, true);
    } catch (e) {
      console.error('Zuzify OAuth connection rejected:', e?.code || e?.message || 'unknown');
      return finishPage(res, false);
    }
  }

  async function unlink(req, res) {
    const license = await sessionFrom(req);
    const client = await db.connect();
    try {
      await client.query('BEGIN');
      await client.query('DELETE FROM zora_zuzify_links WHERE license_id=$1', [license.id]);
      await audit(client, license.id, 'zuzify.unlinked', license.username);
      await client.query('COMMIT');
    } catch (e) { await client.query('ROLLBACK'); throw e; }
    finally { client.release(); }
    return json(res, 200, { linked: false, zuzify: null });
  }

  return function zuzifyHandler(req, res, path, url) {
    if (req.method === 'GET' && path === '/v1/zuzify/callback') return callback(url, res);
    if (req.method === 'GET' && path === '/v1/zuzify/me') return me(req, res);
    if (req.method === 'POST' && path === '/v1/zuzify/start') return start(req, res);
    if (req.method === 'POST' && path === '/v1/zuzify/unlink') return unlink(req, res);
    throw error(404, 'not_found', 'Zuzify endpoint not found');
  };
}
