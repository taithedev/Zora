#!/usr/bin/env node
import fs from 'node:fs/promises';
const API = (process.env.ZORA_API_URL || 'http://127.0.0.1:8787').replace(/\/$/, '');
const TOKEN = process.env.ZORA_ADMIN_TOKEN;
const [command, ...args] = process.argv.slice(2);
const usage = `Zora owner console — $25 licenses

  node zora-admin.mjs stats
  node zora-admin.mjs create [quantity=1] [devices=1] [--output keys.json]
  node zora-admin.mjs list
  node zora-admin.mjs ban <license-uuid>
  node zora-admin.mjs unban <license-uuid>
  node zora-admin.mjs revoke <license-uuid>
  node zora-admin.mjs reset-devices <license-uuid>
  node zora-admin.mjs audit

Set ZORA_API_URL (hosted HTTPS API) and ZORA_ADMIN_TOKEN in the owner environment.
Never ship the admin token with the desktop client.`;
async function request(path, method = 'GET', payload) {
  const res = await fetch(`${API}${path}`, {
    method,
    headers: { 'content-type': 'application/json', 'x-zora-admin-token': TOKEN },
    body: payload ? JSON.stringify(payload) : undefined,
    signal: AbortSignal.timeout(15000),
  });
  const value = await res.json();
  if (!res.ok) throw Error(value.message || `HTTP ${res.status}`);
  return value;
}
try {
  if (!command || command === 'help' || command === '--help') { console.log(usage); process.exit(0); }
  if (!TOKEN || TOKEN.length < 24) throw new Error('Set ZORA_ADMIN_TOKEN (24+ characters) before using the owner console.');
  if (command === 'stats') console.table([await request('/v1/admin/stats')]);
  else if (command === 'list') {
    const { licenses } = await request('/v1/admin/licenses?limit=200');
    console.table(licenses.map(x => ({ id: x.id, key: x.key_label, username: x.username || '-', status: x.status, devices: x.devices, created: x.created_at, lastSeen: x.last_seen_at })));
  } else if (command === 'audit') {
    const { events } = await request('/v1/admin/audit');
    console.table(events.map(x => ({ at: x.occurred_at, action: x.action, actor: x.actor, licenseId: x.license_id })));
  } else if (command === 'create') {
    const quantity = Number(args[0] || 1), maxDevices = Number(args[1] || 1);
    const outIndex = args.indexOf('--output');
    if (outIndex >= 0 && !args[outIndex + 1]) throw new Error('Specify a filename after --output');
    const { licenses } = await request('/v1/admin/licenses/bulk', 'POST', { quantity, maxDevices });
    if (outIndex >= 0) {
      const path = args[outIndex + 1];
      await fs.writeFile(path, JSON.stringify({ createdAt: new Date().toISOString(), priceUsd: 25, licenses }, null, 2), { flag: 'wx', mode: 0o600 });
      console.log(`Generated ${licenses.length} licenses and saved them to ${path}. Keep this file private.`);
    } else {
      console.table(licenses);
      console.log('Save these keys securely now. Zora stores only hashes and cannot show them again.');
    }
  } else if (['ban', 'unban', 'revoke'].includes(command)) {
    const id = args[0]; if (!/^[0-9a-f-]{36}$/i.test(id || '')) throw new Error('Provide the license UUID from `list`.');
    const status = command === 'unban' ? 'active' : command === 'ban' ? 'banned' : 'revoked';
    console.log(await request(`/v1/admin/licenses/${id}/status`, 'POST', { status }));
  } else if (command === 'reset-devices') {
    const id = args[0]; if (!/^[0-9a-f-]{36}$/i.test(id || '')) throw new Error('Provide the license UUID from `list`.');
    console.log(await request(`/v1/admin/licenses/${id}/reset-devices`, 'POST', {}));
  } else { console.error(usage); process.exitCode = 1; }
} catch (e) { console.error(`Zora admin: ${e.message}`); process.exitCode = 1; }
