import test from 'node:test';
import assert from 'node:assert/strict';
import { hashOwnerPassword, verifyOwnerPassword, createOwnerHandler } from '../src/owner.mjs';

test('owner password scrypt salted hash never contains plaintext',async()=>{
  const pass='aSufficientlyLongOwnerPassword_2026';
  const first=await hashOwnerPassword(pass),second=await hashOwnerPassword(pass);
  assert.match(first,/^scrypt\$v1\$[a-f0-9]{48}\$[a-f0-9]{128}$/);
  assert.notEqual(first,second);
  assert.equal(first.includes(pass),false);
  assert.equal(await verifyOwnerPassword(pass,first),true);
  assert.equal(await verifyOwnerPassword('wrong-password',first),false);
  assert.equal(await verifyOwnerPassword(pass,second),true);
});
test('password validation rejects weak and malformed hashes',async()=>{
  await assert.rejects(hashOwnerPassword('short'));
  assert.equal(await verifyOwnerPassword('anything','not-a-hash'),false);
  assert.equal(await verifyOwnerPassword(null,'scrypt$v1$x$y'),false);
});
test('private admin routes require a bearer session and ignore legacy API tokens',async()=>{
  const fakeDb={query:async()=>{throw Error('unauthorized request should not query database');}};
  const handle=createOwnerHandler({db:fakeDb,json(){throw Error('should never send success')},error:(status,code,message)=>Object.assign(new Error(message),{status,code}),body:async()=>({}),audit:async()=>{},createLicenses:async()=>[]});
  await assert.rejects(handle({method:'GET',headers:{'x-zora-admin-token':'old-secret'}}, {}, '/v1/admin/stats', new URL('https://neon.example/v1/admin/stats')),err=>err.status===401);
});
