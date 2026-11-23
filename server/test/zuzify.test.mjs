import assert from 'node:assert/strict';
import test from 'node:test';
import { createHash } from 'node:crypto';
import { createZuzifyHandler } from '../src/zuzify.mjs';
import { signAccess } from '../src/crypto.mjs';

const secret = 'this-is-a-long-unique-test-session-secret';
const licenseId = '90d94ca5-ccbb-4fd6-9b21-2370f4cebbbb';
const accountId = 'f991a50c-f5da-4f33-a957-c14c47e95ba1';
const hash = input => createHash('sha256').update(input).digest('hex');

function service() {
  const state = { pending: null, identity: null, requests: 0 };
  const db = {
    async query(sql, args = []) {
      if (['BEGIN','COMMIT','ROLLBACK'].includes(sql)) return { rows: [] };
      if (sql.includes('FROM zora_licenses l') && sql.includes('JOIN zora_devices')) {
        return { rows: [{ id: licenseId, username: 'tester' }] };
      }
      if (sql.startsWith('SELECT 1 FROM zora_zuzify_links')) return { rows: state.identity ? [{}] : [] };
      if (sql.startsWith('SELECT zuzify_user_id')) return { rows: state.identity ? [{ zuzify_user_id: state.identity, linked_at: new Date().toISOString() }] : [] };
      if (sql.startsWith('DELETE FROM zora_zuzify_link_requests')) { state.pending = null; return { rows: [] }; }
      if (sql.startsWith('INSERT INTO zora_zuzify_link_requests')) {
        state.pending = { hash: args[0], verifier: args[2] }; return { rows: [] };
      }
      if (sql.startsWith('UPDATE zora_zuzify_link_requests')) {
        if (!state.pending || state.pending.hash !== args[0]) return { rows: [] };
        const pending = state.pending; state.pending = null;
        return { rows: [{ license_id: licenseId, code_verifier: pending.verifier }] };
      }
      if (sql.startsWith('SELECT id,username FROM zora_licenses')) return { rows: [{id: licenseId,username: 'tester'}] };
      if (sql.startsWith('INSERT INTO zora_zuzify_links')) { state.identity = args[1]; return { rows: [] }; }
      if (sql.startsWith('DELETE FROM zora_zuzify_links')) { state.identity = null; return { rows: [] }; }
      throw new Error('Unexpected SQL: ' + sql);
    },
    async connect() { return { query: (sql, args) => db.query(sql, args), release() {} }; }
  };
  let response;
  const res = () => ({ writeHead(status,headers){response={status,headers};},end(data){response.data=data;} });
  const error = (status,code,message) => Object.assign(new Error(message),{status,code});
  const json = (out,status,payload) => { out.writeHead(status,{});out.end(JSON.stringify(payload)); };
  const bearer = 'Bearer ' + signAccess(secret,{licenseId,deviceHash:'hash',username:'tester'});
  const fetcher = async (url, options) => {
    state.requests++;
    if (url.endsWith('/oauth2/token')) {
      assert.equal(options.body.get('client_id'),'test-zuzify-client');
      assert.equal(options.body.get('resource'),'https://zuzify.xyz');
      assert.ok(options.body.get('code_verifier').length>=43);
      return { ok: true, json: async()=>({access_token:'verified-user-token'}) };
    }
    if (url.endsWith('/oauth2/userinfo')) {
      assert.equal(options.headers.authorization,'Bearer verified-user-token');
      return { ok: true, json: async()=>({sub:accountId}) };
    }
    throw new Error('Unexpected fetch');
  };
  const handler = createZuzifyHandler({ db,json,error,audit:async()=>{},
    sessionSecret:secret,fetcher,env:{
      ZUZIFY_AUTH_URL:'https://auth.example/neondb/auth',
      ZUZIFY_OAUTH_CLIENT_ID:'test-zuzify-client',
      ZUZIFY_REDIRECT_URI:'https://zora.example/v1/zuzify/callback'
    }
  });
  const call = async (method,path,auth='',search='') => {
    const output=res();
    await handler({method,headers:auth?{authorization:auth}:{}},output,path,new URL('https://zora.example'+path+search));
    return response;
  };
  return { call,bearer,state };
}

test('Zuzify linking is restricted to an active Zora license',async()=>{
  const s=service();
  await assert.rejects(()=>s.call('POST','/v1/zuzify/start'),e=>e.status===401);
});
test('Zuzify OAuth uses PKCE, verifies userinfo, and prevents state replay',async()=>{
  const s=service();
  const started=await s.call('POST','/v1/zuzify/start',s.bearer);
  assert.equal(started.status,200);
  const auth=new URL(JSON.parse(started.data).url);
  assert.equal(auth.searchParams.get('scope'),'openid profile');
  assert.equal(auth.searchParams.get('resource'),'https://zuzify.xyz');
  assert.equal(auth.searchParams.get('code_challenge_method'),'S256');
  const state=auth.searchParams.get('state');
  assert.equal(s.state.pending.hash,hash(state));
  const expectedChallenge=createHash('sha256').update(s.state.pending.verifier).digest('base64url');
  assert.equal(auth.searchParams.get('code_challenge'),expectedChallenge);
  const cb=await s.call('GET','/v1/zuzify/callback','',`?state=${state}&code=one-time-code`);
  assert.equal(cb.status,200);
  const me=await s.call('GET','/v1/zuzify/me',s.bearer);
  assert.equal(JSON.parse(me.data).zuzify.userId,accountId);
  const replay=await s.call('GET','/v1/zuzify/callback','',`?state=${state}&code=one-time-code`);
  assert.equal(replay.status,400);
  assert.equal(s.state.requests,2,'replayed state must not call auth endpoints');
  const unlinked=await s.call('POST','/v1/zuzify/unlink',s.bearer);
  assert.equal(unlinked.status,200);
  assert.equal(JSON.parse((await s.call('GET','/v1/zuzify/me',s.bearer)).data).linked,false);
});
test('Zuzify callback rejects missing or malformed one-time state',async()=>{
  const s=service();
  const output=await s.call('GET','/v1/zuzify/callback','','?state=fake&code=bad');
  assert.equal(output.status,400);
  assert.equal(s.state.requests,0);
});
