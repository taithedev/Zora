// One-time password enrollment for Neon owner administration.
// Run from the server directory using DATABASE_URL from the Neon console.
// Requires an interactive terminal; never accepts the password in CLI arguments.
import pg from 'pg';
import { hashOwnerPassword } from '../src/owner.mjs';

async function hidden(label) {
  if (!process.stdin.isTTY) throw Error('Use an interactive terminal. Password input is hidden.');
  process.stdout.write(label);
  process.stdin.setRawMode(true);process.stdin.resume();
  let result='';
  try {
    for await(const chunk of process.stdin) {
      for(const character of String(chunk)) {
        if(character==='\r'||character==='\n'){process.stdout.write('\n');return result;}
        if(character==='\u0003')throw Error('Cancelled');
        if(character==='\u007f')result=result.slice(0,-1);
        else if(character.charCodeAt(0)>=32 && result.length<128)result+=character;
      }
    }
  } finally {process.stdin.setRawMode(false);process.stdin.pause();}
  return result;
}
if(!process.env.DATABASE_URL)throw Error('Set DATABASE_URL from your Neon console first.');
const first=await hidden('Choose owner console password (16+ characters): ');
const second=await hidden('Confirm owner console password: ');
if(first!==second)throw Error('Passwords did not match.');
const passwordHash=await hashOwnerPassword(first);
const pool=new pg.Pool({connectionString:process.env.DATABASE_URL,ssl:{rejectUnauthorized:true}});
try {
  const c=await pool.connect();
  try {
    await c.query('BEGIN');
    await c.query(`INSERT INTO zora_owner_auth(id,password_hash) VALUES (1,$1)
      ON CONFLICT (id) DO UPDATE SET password_hash=EXCLUDED.password_hash,password_version=zora_owner_auth.password_version+1,
      failed_attempts=0,locked_until=NULL,updated_at=now()`,[passwordHash]);
    await c.query('UPDATE zora_owner_sessions SET revoked_at=now() WHERE revoked_at IS NULL');
    await c.query(`INSERT INTO zora_audit(license_id,action,actor) VALUES (NULL,'owner.password_set','owner-setup')`);
    await c.query('COMMIT');
    console.log('Owner password stored in Neon as a salted scrypt hash. Previous sessions revoked.');
  } catch(e) {await c.query('ROLLBACK');throw e;}finally{c.release();}
} finally {await pool.end();}
