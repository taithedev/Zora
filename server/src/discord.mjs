import { randomBytes } from 'node:crypto';
import { sha256, safeEqual, verifyAccess } from './crypto.mjs';

const api = 'https://discord.com/api/v10';
const allowedId = /^\d{15,25}$/;
const allowedState = /^[A-Za-z0-9_-]{35,128}$/;
const htmlHeaders = {
  'content-type': 'text/html; charset=utf-8',
  'cache-control': 'no-store',
  'referrer-policy': 'no-referrer',
  'x-content-type-options': 'nosniff',
  'x-frame-options': 'DENY',
  'content-security-policy': "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'"
};
function finishPage(res, ok) {
  const title = ok ? 'Discord connected' : 'Could not connect Discord';
  const detail = ok
    ? 'Your Discord account has been linked to Zora. Return to the Zora app.'
    : 'The link expired or was rejected. Return to Zora and try again.';
  res.writeHead(ok ? 200 : 400, htmlHeaders);
  res.end(`<!doctype html><html lang="en"><meta charset="utf-8"><title>${title}</title><body style="margin:0;background:#050505;color:white;font:16px system-ui;min-height:100vh;display:grid;place-items:center"><main style="max-width:520px;padding:38px"><h1>${title}</h1><p>${detail}</p></main></body></html>`);
}

/**
 * Discord OAuth2 authorization-code flow. The browser never receives a Zora token
 * and the desktop app never receives Discord OAuth credentials or user tokens.
 * This module can be tested with a synthetic database and fetch implementation.
 */
export function createDiscordHandler({ db, json, error, audit, sessionSecret, fetcher = fetch, env = process.env }) {
  const clientId = env.DISCORD_CLIENT_ID || '';
  const clientSecret = env.DISCORD_CLIENT_SECRET || '';
  const redirectUri = env.DISCORD_REDIRECT_URI || '';
  const botSecret = env.ZORA_BOT_SYNC_SECRET || '';
  const configReady = /^\d{15,25}$/.test(clientId)
    && clientSecret.length >= 20
    && /^https:\/\//.test(redirectUri)
    && new URL(redirectUri).pathname.endsWith('/v1/discord/callback');

  async function sessionFrom(req) {
    const token = String(req.headers.authorization || '').replace(/^Bearer\s+/i, '');
    const value = verifyAccess(sessionSecret, token);
    if (!value?.licenseId || !value?.deviceHash || !value?.username) {
      throw error(401, 'invalid_session', 'Please sign in to Zora again');
    }
    const { rows } = await db.query(`SELECT l.id, l.username FROM zora_licenses l
      JOIN zora_devices d ON d.license_id = l.id
      WHERE l.id=$1 AND d.device_hash=$2 AND l.status='active'
      AND (l.expires_at IS NULL OR l.expires_at > now())`, [value.licenseId, value.deviceHash]);
    if (!rows[0] || rows[0].username !== value.username) {
      throw error(401, 'invalid_session', 'Zora license is no longer active');
    }
    return rows[0];
  }

  async function me(req, res) {
    const license = await sessionFrom(req);
    const { rows } = await db.query(`SELECT discord_user_id, discord_username,
      discord_display_name, discord_avatar_hash, linked_at
      FROM zora_discord_links WHERE license_id=$1`, [license.id]);
    const linked = rows[0] || null;
    return json(res, 200, { linked: Boolean(linked), discord: linked && {
      id: linked.discord_user_id,
      username: linked.discord_username,
      displayName: linked.discord_display_name,
      avatarHash: linked.discord_avatar_hash,
      linkedAt: linked.linked_at
    }});
  }

  async function start(req, res) {
    const license = await sessionFrom(req);
    if (!configReady) throw error(503, 'discord_unavailable', 'Discord linking is not configured yet');
    const { rows } = await db.query('SELECT 1 FROM zora_discord_links WHERE license_id=$1', [license.id]);
    if (rows.length) throw error(409, 'already_linked', 'Unlink your current Discord account first');
    const state = randomBytes(32).toString('base64url');
    await db.query('DELETE FROM zora_discord_link_requests WHERE license_id=$1 OR expires_at < now()', [license.id]);
    await db.query(`INSERT INTO zora_discord_link_requests(state_hash, license_id, expires_at)
      VALUES($1,$2,now() + interval '10 minutes')`, [sha256(state), license.id]);
    const authorization = new URL('https://discord.com/oauth2/authorize');
    authorization.searchParams.set('response_type', 'code');
    authorization.searchParams.set('client_id', clientId);
    authorization.searchParams.set('scope', 'identify');
    authorization.searchParams.set('redirect_uri', redirectUri);
    authorization.searchParams.set('state', state);
    authorization.searchParams.set('prompt', 'consent');
    return json(res, 200, { url: authorization.toString(), expiresIn: 600 });
  }

  async function callback(url, res) {
    const state = url.searchParams.get('state') || '';
    const code = url.searchParams.get('code') || '';
    if (!configReady || !allowedState.test(state) || !code || code.length > 2048 || url.searchParams.has('error')) {
      return finishPage(res, false);
    }
    // Claim exactly once before exchanging the code. A reused state cannot link any account.
    const claim = await db.query(`UPDATE zora_discord_link_requests
      SET consumed_at=now()
      WHERE state_hash=$1 AND consumed_at IS NULL AND expires_at>now()
      RETURNING license_id`, [sha256(state)]);
    if (!claim.rows[0]) return finishPage(res, false);
    try {
      const form = new URLSearchParams({
        client_id: clientId, client_secret: clientSecret,
        grant_type: 'authorization_code', code, redirect_uri: redirectUri
      });
      const tokenResponse = await fetcher(`${api}/oauth2/token`, {
        method: 'POST', headers: { 'content-type': 'application/x-www-form-urlencoded' },
        body: form, signal: AbortSignal.timeout(10000)
      });
      if (!tokenResponse.ok) return finishPage(res, false);
      const token = await tokenResponse.json();
      if (typeof token.access_token !== 'string' || token.access_token.length < 10) return finishPage(res, false);
      const userResponse = await fetcher(`${api}/users/@me`, {
        headers: { authorization: `Bearer ${token.access_token}` },
        signal: AbortSignal.timeout(10000)
      });
      if (!userResponse.ok) return finishPage(res, false);
      const user = await userResponse.json();
      if (!allowedId.test(String(user.id)) || typeof user.username !== 'string') return finishPage(res, false);
      // Each Discord identity can be linked to only one license (UNIQUE constraint).
      // Duplicate-link conflicts are intentionally not shown to the browser.
      const client = await db.connect();
      try {
        await client.query('BEGIN');
        const verified = await client.query(`SELECT id, username FROM zora_licenses WHERE id=$1
          AND status='active' AND (expires_at IS NULL OR expires_at>now()) FOR UPDATE`, [claim.rows[0].license_id]);
        if (!verified.rows[0]) throw new Error('License is unavailable');
        await client.query(`INSERT INTO zora_discord_links
          (license_id,discord_user_id,discord_username,discord_display_name,discord_avatar_hash)
          VALUES($1,$2,$3,$4,$5)
          ON CONFLICT(license_id) DO UPDATE SET
            discord_user_id=EXCLUDED.discord_user_id,
            discord_username=EXCLUDED.discord_username,
            discord_display_name=EXCLUDED.discord_display_name,
            discord_avatar_hash=EXCLUDED.discord_avatar_hash,
            linked_at=now()`, [
          claim.rows[0].license_id, user.id, user.username.slice(0, 64),
          typeof user.global_name === 'string' ? user.global_name.slice(0,100) : null,
          typeof user.avatar === 'string' ? user.avatar.slice(0,128) : null
        ]);
        await audit(client, claim.rows[0].license_id, 'discord.linked', verified.rows[0].username,
          { discordId: user.id });
        await client.query('COMMIT');
      } catch (e) { await client.query('ROLLBACK'); throw e; }
      finally { client.release(); }
      return finishPage(res, true);
    } catch (e) {
      console.error('Discord OAuth link failed:', e?.message || 'unknown');
      return finishPage(res, false);
    }
  }

  async function unlink(req, res) {
    const license = await sessionFrom(req);
    const client = await db.connect();
    try {
      await client.query('BEGIN');
      await client.query('DELETE FROM zora_discord_links WHERE license_id=$1', [license.id]);
      await audit(client, license.id, 'discord.unlinked', license.username);
      await client.query('COMMIT');
    } catch (e) { await client.query('ROLLBACK'); throw e; }
    finally { client.release(); }
    return json(res, 200, { linked: false });
  }

  async function lookupForBot(req, res, id) {
    if (!allowedId.test(id)) throw error(400, 'bad_discord_id', 'Invalid Discord ID');
    const auth = String(req.headers.authorization || '');
    if (botSecret.length < 32 || !auth.startsWith('Bot ') || !safeEqual(botSecret, auth.slice(4))) {
      throw error(401, 'unauthorized', 'Invalid bot credentials');
    }
    const { rows } = await db.query(`SELECT l.username,dl.linked_at FROM zora_discord_links dl
      JOIN zora_licenses l ON l.id=dl.license_id
      WHERE dl.discord_user_id=$1 AND l.status='active'
      AND (l.expires_at IS NULL OR l.expires_at>now())`, [id]);
    return json(res, 200, { linked: Boolean(rows[0]),
      username: rows[0]?.username || null, linkedAt: rows[0]?.linked_at || null });
  }

  return async function discordHandler(req, res, path, url) {
    if (req.method === 'GET' && path === '/v1/discord/callback') return callback(url, res);
    if (req.method === 'GET' && path === '/v1/discord/me') return me(req, res);
    if (req.method === 'POST' && path === '/v1/discord/start') return start(req, res);
    if (req.method === 'POST' && path === '/v1/discord/unlink') return unlink(req, res);
    if (req.method === 'GET' && /^\/v1\/discord\/bot\/\d{15,25}$/.test(path)) {
      return lookupForBot(req, res, path.split('/').at(-1));
    }
    throw error(404, 'not_found', 'Discord endpoint not found');
  };
}
