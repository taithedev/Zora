import assert from 'node:assert/strict';
import test from 'node:test';
import { createDiscordHandler } from '../src/discord.mjs';
import { sha256, signAccess } from '../src/crypto.mjs';

const secret = 'this-is-a-long-unique-test-session-secret';
const licenseId = '90d94ca5-ccbb-4fd6-9b21-2370f4cebbbb';
const discordId = '1052713883753775164';
function service() {
  const state = { links: [], pending: [], requests: 0 };
  const db = {
    async query(sql, args = []) {
      if (['BEGIN', 'COMMIT', 'ROLLBACK'].includes(sql)) return {rows:[]};
      if (sql.includes('FROM zora_licenses l') && sql.includes('JOIN zora_devices')) return {rows:[{id:licenseId, username:'tester'}]};
      if (sql.startsWith('SELECT discord_user_id')) return {rows:state.links.map(x => ({discord_user_id:x.id,discord_username:x.username,linked_at:Date.now()}))};
      if (sql.startsWith('SELECT 1 FROM zora_discord_links')) return {rows: state.links.length ? [{exists:1}] : []};
      if (sql.startsWith('DELETE FROM zora_discord_link_requests')) {state.pending=[]; return {rows:[]};}
      if (sql.startsWith('INSERT INTO zora_discord_link_requests')) {state.pending.push(args[0]); return {rows:[]};}
      if (sql.startsWith('UPDATE zora_discord_link_requests')) {
        const found=state.pending.includes(args[0]);
        if (found) state.pending.splice(state.pending.indexOf(args[0]),1);
        return {rows:found?[{license_id:licenseId}]:[]};
      }
      if (sql.startsWith('SELECT id, username FROM zora_licenses')) return {rows:[{id:licenseId,username:'tester'}]};
      if (sql.startsWith('INSERT INTO zora_discord_links')) {state.links = [{id:args[1],username:args[2]}]; return {rows:[]};}
      if (sql.startsWith('DELETE FROM zora_discord_links')) {state.links=[]; return {rows:[]};}
      if (sql.includes('FROM zora_discord_links dl')) return {rows:state.links.length?[{username:'tester',linked_at:Date.now()}]:[]};
      if (sql.startsWith('INSERT INTO zora_audit')) return {rows:[]};
      throw new Error(`Unexpected query: ${sql}`);
    },
    async connect() { return {query: (s,a)=>db.query(s,a), release(){}}; }
  };
  let response;
  const res = () => ({writeHead(status,headers){response={status,headers};},end(data){response.data=data;}});
  const err=(status, code, message)=>Object.assign(new Error(message), {status,code});
  const json=(output,status,object)=>{output.writeHead(status,{'content-type':'application/json'});output.end(JSON.stringify(object));};
  const bearer = `Bearer ${signAccess(secret,{licenseId,deviceHash:'hash',username:'tester'})}`;
  const fetcher=async (url,options)=>{
    state.requests++;
    if (url.endsWith('/oauth2/token')) return {ok:true,json:async()=>({access_token:'test-oauth-access-token'})};
    if (url.endsWith('/users/@me')) {
      assert.equal(options.headers.authorization,'Bearer test-oauth-access-token');
      return {ok:true,json:async()=>({id:discordId,username:'person',global_name:'Person'})};
    }
    throw new Error('Unexpected fetch');
  };
  const handler=createDiscordHandler({db,json,error:err,sessionSecret:secret,fetcher,
    audit:async()=>{},env:{
      DISCORD_CLIENT_ID:'1560092882684219463',
      DISCORD_CLIENT_SECRET:'fake-secret-length-longer-than-20',
      DISCORD_REDIRECT_URI:'https://zora.example/v1/discord/callback',
      ZORA_BOT_SYNC_SECRET:'long-unique-secret-for-bot-in-test-0001'
    }});
  return {handler,res,read:()=>response,bearer,state};
}
async function call(s, method, path, auth='', search='') {
  const res=s.res();
  await s.handler({method, headers:auth?{authorization:auth}:{}} ,res,path,new URL(`https://zora.example${path}${search}`));
  return s.read();
}

test('Discord linking requires an active Zora session', async()=>{
  const s=service();
  await assert.rejects(()=>call(s,'POST','/v1/discord/start'),x=>x.status===401);
});
test('Discord links require consent, one-time state, and return only basic identity', async()=>{
  const s=service();
  let r=await call(s,'POST','/v1/discord/start',s.bearer);
  assert.equal(r.status,200);
  const auth=new URL(JSON.parse(r.data).url);
  assert.equal(auth.hostname,'discord.com');
  assert.equal(auth.searchParams.get('scope'),'identify');
  const state=auth.searchParams.get('state');
  assert.ok(s.state.pending.includes(sha256(state)));
  r=await call(s,'GET','/v1/discord/callback','',`?code=example-code&state=${state}`);
  assert.equal(r.status,200);
  assert.equal(s.state.requests,2);
  r=await call(s,'GET','/v1/discord/me',s.bearer);
  assert.deepEqual(JSON.parse(r.data).discord.username,'person');
  assert.equal(JSON.parse(r.data).linked,true);
  r=await call(s,'GET','/v1/discord/callback','',`?code=example-code&state=${state}`);
  assert.equal(r.status,400);
  assert.equal(s.state.requests,2,'reused OAuth state must not contact Discord');
  r=await call(s,'GET',`/v1/discord/bot/${discordId}`,'Bot long-unique-secret-for-bot-in-test-0001');
  assert.equal(JSON.parse(r.data).username,'tester');
  r=await call(s,'POST','/v1/discord/unlink',s.bearer);
  assert.equal(r.status,200);
  r=await call(s,'GET','/v1/discord/me',s.bearer);
  assert.equal(JSON.parse(r.data).linked,false);
});
test('bot lookup never accepts a normal license token', async()=>{
  const s=service();
  await assert.rejects(()=>call(s,'GET',`/v1/discord/bot/${discordId}`,s.bearer),x=>x.status===401);
});
