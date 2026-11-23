#!/usr/bin/env node
// Legacy Node.js CLI, updated to use the same password-only Neon owner sessions.
// For the full menu and exports, use the native ZoraAdmin.exe.
import fs from 'node:fs/promises';
import readline from 'node:readline';
const API = (process.env.ZORA_OWNER_API_URL || 'https://br-frosty-bar-bbmdnhr7-zoralicense.compute.c-15.us-east-1.aws.neon.tech').replace(/\/$/, '');
const [command, ...args] = process.argv.slice(2);
const usage = `Zora owner CLI — password verified inside Neon
  node zora-admin.mjs stats
  node zora-admin.mjs list
  node zora-admin.mjs audit
  node zora-admin.mjs create [count=1] [devices=1] --output keys.json
  node zora-admin.mjs ban|unban|revoke <license-uuid>
  node zora-admin.mjs reset-devices <license-uuid>
For 49 owner commands use the native Windows ZoraAdmin.exe. No API token needed.`;
async function hiddenPassword() {
  if (!process.stdin.isTTY) throw new Error('Start the CLI in an interactive terminal for hidden password entry.');
  process.stdout.write('Owner password: ');
  const stdin = process.stdin;
  stdin.setRawMode(true);stdin.resume();
  let input = '';
  try {
    for await (const chunk of stdin) {
      for (const char of String(chunk)) {
        if (char === '\r' || char === '\n') { process.stdout.write('\n');return input; }
        if (char === '\u0003') throw new Error('Cancelled');
        if (char === '\u007f') input=input.slice(0,-1);
        else if (char.charCodeAt(0)>=32 && input.length<128) input+=char;
      }
    }
  } finally { stdin.setRawMode(false);stdin.pause(); }
  return input;
}
async function request(path, method='GET', value, token='') {
  const response=await fetch(API+path, {method, headers:{'content-type':'application/json',...(token?{authorization:'Bearer '+token}:{})},body:value?JSON.stringify(value):undefined,signal:AbortSignal.timeout(20000)});
  const data=await response.json();
  if (!response.ok) throw new Error(data.message || 'HTTP '+response.status);
  return data;
}
try {
  if (!command || command==='help') { console.log(usage);process.exit(0); }
  const password=await hiddenPassword();
  const {accessToken:token}=await request('/v1/admin/login','POST',{password});
  try {
    if (command==='stats') console.table([await request('/v1/admin/stats','GET',undefined,token)]);
    else if (command==='list') console.table((await request('/v1/admin/licenses?limit=200','GET',undefined,token)).licenses);
    else if (command==='audit') console.table((await request('/v1/admin/audit','GET',undefined,token)).events);
    else if (command==='create') {
      const quantity=Number(args[0]||1),maxDevices=Number(args[1]||1);
      const index=args.indexOf('--output');
      if(index===-1||!args[index+1])throw new Error('Pass --output FILE to save each plaintext key exactly once.');
      const data=await request('/v1/admin/licenses/bulk','POST',{quantity,maxDevices},token);
      await fs.writeFile(args[index+1],JSON.stringify({licenses:data.licenses,createdAt:new Date().toISOString(),priceUsd:25},null,2),{flag:'wx',mode:0o600});
      console.log(`Saved ${data.licenses.length} keys to a new private file. Keys cannot be retrieved again.`);
    } else if (['ban','unban','revoke','reset-devices'].includes(command)) {
      const id=args[0];if(!/^[0-9a-f-]{36}$/i.test(id||''))throw new Error('Enter the license UUID.');
      const path=command==='reset-devices'?`/v1/admin/licenses/${id}/reset-devices`:`/v1/admin/licenses/${id}/status`;
      const payload=command==='reset-devices'?{}:{status:command==='unban'?'active':command==='ban'?'banned':'revoked'};
      console.log(await request(path,'POST',payload,token));
    } else throw new Error('Unknown command. See help.');
  } finally {await request('/v1/admin/logout','POST',{},token).catch(()=>{});}
} catch(err){console.error('Zora owner:',err.message);process.exitCode=1;}
