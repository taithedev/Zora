import { createHash, randomBytes, scrypt as callbackScrypt, timingSafeEqual } from 'node:crypto';
import { promisify } from 'node:util';
const scrypt = promisify(callbackScrypt);
const hash = value => createHash('sha256').update(value).digest('hex');
const passwordPattern = value => typeof value === 'string' && value.length >= 16 && value.length <= 128;
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const allowedStatuses = new Set(['active','banned','revoked']);
const TOKEN_TTL_HOURS = 8;

// These functions are exported so password hashing is testable independently of Neon.
export async function hashOwnerPassword(password) {
  if (!passwordPattern(password)) throw new Error('Owner password must contain 16 to 128 characters');
  const salt = randomBytes(24);
  const key = await scrypt(password, salt, 64, { N: 16384, r: 8, p: 1, maxmem: 64 * 1024 * 1024 });
  return `scrypt$v1$${salt.toString('hex')}$${key.toString('hex')}`;
}
export async function verifyOwnerPassword(password, stored) {
  if (typeof password !== 'string' || password.length > 256 || typeof stored !== 'string') return false;
  const parts = stored.split('$');
  if (parts.length !== 4 || parts[0] !== 'scrypt' || parts[1] !== 'v1' ||
      !/^[0-9a-f]{48}$/.test(parts[2]) || !/^[0-9a-f]{128}$/.test(parts[3])) return false;
  const expected = Buffer.from(parts[3], 'hex');
  const actual = await scrypt(password, Buffer.from(parts[2], 'hex'), 64,
    { N: 16384, r: 8, p: 1, maxmem: 64 * 1024 * 1024 });
  return timingSafeEqual(expected, actual);
}

export function createOwnerHandler({ db, json, error, body, audit, createLicenses }) {
  const assertUuid = id => {
    if (!uuidPattern.test(id ?? '')) throw error(400, 'invalid_id', 'Invalid license or session ID');
    return id;
  };
  const limitValue = (v, fallback = 50, max = 500) => {
    const n = Number(v);
    return !v || !Number.isFinite(n) ? fallback : Math.min(max, Math.max(1, Math.floor(n)));
  };
  const offsetValue = v => {
    const n = Number(v);
    return Number.isFinite(n) ? Math.min(1000000, Math.max(0, Math.floor(n))) : 0;
  };
  const payload = async req => {
    const parsed = await body(req);
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed))
      throw error(400, 'invalid_body', 'Expected a JSON object');
    return parsed;
  };
  async function ownerSession(req) {
    const auth = req.headers.authorization;
    const token = typeof auth === 'string' && auth.match(/^Bearer\s+([A-Za-z0-9_-]{40,128})$/i)?.[1];
    if (!token) throw error(401, 'owner_login_required', 'Enter your owner password to continue');
    const { rows } = await db.query(`SELECT s.id,s.created_at,s.expires_at,s.last_seen_at
      FROM zora_owner_sessions s JOIN zora_owner_auth a ON a.id=1
      WHERE s.token_hash=$1 AND s.revoked_at IS NULL AND s.expires_at>now()
        AND s.password_version=a.password_version`, [hash(token)]);
    if (!rows[0]) throw error(401, 'owner_session_expired', 'Session expired; enter your password again');
    await db.query(`UPDATE zora_owner_sessions SET last_seen_at=now()
      WHERE id=$1 AND last_seen_at<now()-interval '5 minutes'`, [rows[0].id]);
    return { ...rows[0], tokenHash: hash(token) };
  }
  async function login(req, res) {
    const { password } = await payload(req);
    if (typeof password !== 'string' || password.length > 256)
      throw error(400, 'invalid_password', 'Enter your owner password');
    const c = await db.connect();
    try {
      await c.query('BEGIN');
      const { rows } = await c.query('SELECT * FROM zora_owner_auth WHERE id=1 FOR UPDATE');
      const owner = rows[0];
      if (!owner) throw error(503, 'owner_not_configured', 'Owner password is not configured in Neon');
      if (owner.locked_until && new Date(owner.locked_until).getTime() > Date.now())
        throw error(429, 'owner_locked', 'Too many attempts; try again later');
      if (!await verifyOwnerPassword(password, owner.password_hash)) {
        const count = Number(owner.failed_attempts) + 1;
        await c.query(`UPDATE zora_owner_auth SET failed_attempts=$2,
          locked_until=CASE WHEN $2>=5 THEN now()+interval '10 minutes' ELSE NULL END WHERE id=1`,
          [owner.id, count >= 5 ? 0 : count]);
        await audit(c, null, 'owner.login_failed', 'owner');
        await c.query('COMMIT');
        return json(res, 401, { error: 'invalid_password', message: 'Incorrect owner password' });
      }
      await c.query('UPDATE zora_owner_auth SET failed_attempts=0,locked_until=NULL WHERE id=1');
      const token = randomBytes(48).toString('base64url');
      const agent = String(req.headers['user-agent'] || '').slice(0, 180);
      const { rows: sessions } = await c.query(`INSERT INTO zora_owner_sessions(token_hash,password_version,expires_at,user_agent)
        VALUES ($1,$2,now()+interval '8 hours',$3) RETURNING id,expires_at`,
        [hash(token), owner.password_version, agent]);
      // Allow up to 5 concurrent owner consoles; expire older sessions first.
      await c.query(`UPDATE zora_owner_sessions SET revoked_at=now() WHERE id IN
        (SELECT id FROM zora_owner_sessions WHERE revoked_at IS NULL ORDER BY created_at DESC OFFSET 5)`);
      await audit(c, null, 'owner.login', 'owner');
      await c.query('COMMIT');
      return json(res, 200, { accessToken: token, expiresAt: sessions[0].expires_at, sessionId: sessions[0].id });
    } catch (e) { await c.query('ROLLBACK').catch(() => {}); throw e; }
    finally { c.release(); }
  }
  async function listLicenses(url, res) {
    const where = [], args = [];
    const add = (sql, value) => { args.push(value); where.push(sql.replaceAll('?', `$${args.length}`)); };
    const q = (url.searchParams.get('q') || '').trim();
    if (q) {
      if (q.length > 100) throw error(400,'invalid_search','Search too long');
      add(`(l.key_label ILIKE '%' || ? || '%' OR l.username ILIKE '%' || ? || '%' OR l.id::text ILIKE '%' || ? || '%' OR l.note ILIKE '%' || ? || '%')`, q);
      // Replace repeated placeholder for one value using a parameterized position.
      where[where.length - 1] = `(l.key_label ILIKE '%' || $${args.length} || '%' OR l.username ILIKE '%' || $${args.length} || '%' OR l.id::text ILIKE '%' || $${args.length} || '%' OR l.note ILIKE '%' || $${args.length} || '%')`;
    }
    const status = url.searchParams.get('status');
    if (status) {
      if (!allowedStatuses.has(status)) throw error(400,'invalid_status','Invalid status filter');
      add('l.status=?', status);
    }
    const filter = url.searchParams.get('filter');
    if (filter) {
      if (filter === 'activated') where.push('l.username IS NOT NULL');
      else if (filter === 'unactivated') where.push('l.username IS NULL');
      else if (filter === 'inactive') where.push("l.username IS NOT NULL AND (l.last_seen_at IS NULL OR l.last_seen_at<now()-interval '30 days')");
      else if (filter === 'expiring') where.push("l.expires_at BETWEEN now() AND now()+interval '30 days'");
      else if (filter === 'expired') where.push('l.expires_at IS NOT NULL AND l.expires_at <= now()');
      else throw error(400,'invalid_filter','Unknown license filter');
    }
    const predicate = where.length ? 'WHERE ' + where.join(' AND ') : '';
    const sort = ['created_at', 'last_seen_at', 'expires_at', 'username'].includes(url.searchParams.get('sort')) ? url.searchParams.get('sort') : 'created_at';
    const order = url.searchParams.get('order') === 'asc' ? 'ASC' : 'DESC';
    const limit = limitValue(url.searchParams.get('limit'));
    const offset = offsetValue(url.searchParams.get('offset'));
    const { rows: counts } = await db.query(`SELECT COUNT(*)::int AS count FROM zora_licenses l ${predicate}`, args);
    const { rows } = await db.query(`SELECT l.id,l.key_label,l.username,l.status,l.max_devices,l.created_at,l.activated_at,l.last_seen_at,l.expires_at,l.note,
      (SELECT COUNT(*)::int FROM zora_devices d WHERE d.license_id=l.id) AS devices
      FROM zora_licenses l ${predicate} ORDER BY l.${sort} ${order} NULLS LAST,l.id ASC
      LIMIT $${args.length + 1} OFFSET $${args.length + 2}`, [...args, limit, offset]);
    return json(res, 200, { licenses: rows, total: counts[0].count, limit, offset });
  }
  async function changeLicense(id, changes, event) {
    assertUuid(id);
    const c = await db.connect();
    try {
      await c.query('BEGIN');
      const { rows } = await c.query('SELECT id FROM zora_licenses WHERE id=$1 FOR UPDATE', [id]);
      if (!rows.length) throw error(404,'not_found','License not found');
      if (changes.length) {
        const columns = changes.map((pair,i) => `${pair[0]}=$${i+2}`).join(',');
        await c.query(`UPDATE zora_licenses SET ${columns} WHERE id=$1`, [id, ...changes.map(pair => pair[1])]);
      }
      await audit(c,id,event,'owner');
      const updated = await c.query(`SELECT id,key_label,username,status,max_devices,note,expires_at FROM zora_licenses WHERE id=$1`, [id]);
      await c.query('COMMIT');
      return updated.rows[0];
    } catch(e) { await c.query('ROLLBACK'); throw e; }
    finally { c.release(); }
  }
  async function transactional(id, action, work) {
    assertUuid(id);
    const c = await db.connect();
    try {
      await c.query('BEGIN');
      const { rows } = await c.query('SELECT * FROM zora_licenses WHERE id=$1 FOR UPDATE', [id]);
      if (!rows.length) throw error(404,'not_found','License not found');
      const result = await work(c, rows[0]);
      await audit(c,id,action,'owner');
      await c.query('COMMIT');
      return result;
    } catch(e) { await c.query('ROLLBACK'); throw e; }
    finally { c.release(); }
  }
  return async function handle(req, res, path, url) {
    if (req.method==='POST' && path==='/v1/admin/login') return login(req,res);
    const session = await ownerSession(req);
    if (req.method==='GET' && path==='/v1/admin/me') return json(res,200,{authenticated:true, sessionId:session.id,expiresAt:session.expires_at});
    if (req.method==='POST' && path==='/v1/admin/logout') {
      await db.query('UPDATE zora_owner_sessions SET revoked_at=now() WHERE id=$1',[session.id]);
      return json(res,200,{signedOut:true});
    }
    if (req.method==='GET' && path==='/v1/admin/security') {
      const {rows}=await db.query(`SELECT a.updated_at,a.password_version,
        (SELECT COUNT(*)::int FROM zora_owner_sessions s WHERE s.revoked_at IS NULL AND s.expires_at>now() AND s.password_version=a.password_version) active_sessions
        FROM zora_owner_auth a WHERE a.id=1`);
      return json(res,200,{...rows[0],sessionExpiresAt:session.expires_at});
    }
    if (req.method==='GET' && path==='/v1/admin/sessions') {
      const { rows }=await db.query(`SELECT id,created_at,expires_at,last_seen_at,user_agent FROM zora_owner_sessions
        WHERE revoked_at IS NULL AND expires_at>now() ORDER BY created_at DESC LIMIT 20`);
      return json(res,200,{sessions:rows,currentSessionId:session.id});
    }
    if (req.method==='POST' && path==='/v1/admin/sessions/revoke-others') {
      const result=await db.query('UPDATE zora_owner_sessions SET revoked_at=now() WHERE id<>$1 AND revoked_at IS NULL',[session.id]);
      await audit(db,null,'owner.sessions_revoked','owner',{count:result.rowCount});
      return json(res,200,{revoked:result.rowCount});
    }
    if (req.method==='POST' && path==='/v1/admin/sessions/revoke-all') {
      const result=await db.query('UPDATE zora_owner_sessions SET revoked_at=now() WHERE revoked_at IS NULL');
      await audit(db,null,'owner.sessions_revoked_all','owner',{count:result.rowCount});
      return json(res,200,{revoked:result.rowCount,signedOut:true});
    }
    if (req.method==='POST' && path==='/v1/admin/password') {
      const {currentPassword,newPassword}=await payload(req);
      if (!passwordPattern(newPassword)) throw error(400,'weak_password','New password must contain 16-128 characters');
      const c=await db.connect();
      try {
        await c.query('BEGIN');
        const {rows}=await c.query('SELECT password_hash FROM zora_owner_auth WHERE id=1 FOR UPDATE');
        if (!await verifyOwnerPassword(currentPassword,rows[0].password_hash)) throw error(403,'wrong_password','Incorrect current owner password');
        const newHash=await hashOwnerPassword(newPassword);
        await c.query('UPDATE zora_owner_auth SET password_hash=$1,password_version=password_version+1,updated_at=now(),failed_attempts=0,locked_until=NULL WHERE id=1',[newHash]);
        await c.query('UPDATE zora_owner_sessions SET revoked_at=now() WHERE revoked_at IS NULL');
        await audit(c,null,'owner.password_changed','owner');
        await c.query('COMMIT');
        return json(res,200,{updated:true,signedOut:true});
      } catch(e){await c.query('ROLLBACK');throw e;}finally{c.release();}
    }
    if (req.method==='GET' && path==='/v1/admin/stats') {
      const {rows}=await db.query(`SELECT COUNT(*)::int total,
        COUNT(*) FILTER (WHERE username IS NOT NULL)::int activated,
        COUNT(*) FILTER (WHERE username IS NULL)::int unactivated,
        COUNT(*) FILTER (WHERE status='active')::int active,
        COUNT(*) FILTER (WHERE status='banned')::int banned,
        COUNT(*) FILTER (WHERE status='revoked')::int revoked,
        COUNT(*) FILTER (WHERE expires_at IS NOT NULL AND expires_at<=now())::int expired,
        COUNT(*) FILTER (WHERE expires_at BETWEEN now() AND now()+interval '30 days')::int expiring_30_days,
        COUNT(*) FILTER (WHERE created_at>now()-interval '24 hours')::int created_24h,
        COUNT(*) FILTER (WHERE activated_at>now()-interval '24 hours')::int activated_24h,
        (SELECT COUNT(*)::int FROM zora_devices) devices FROM zora_licenses`);
      return json(res,200,{...rows[0],priceUsd:25, estimatedGrossUsd:rows[0].total*25,
        grossDisclaimer:'Generated license value only; not verified payments'});
    }
    if (req.method==='GET' && path==='/v1/admin/activity') {
      const {rows}=await db.query(`SELECT date_trunc('day',occurred_at)::date day,action,COUNT(*)::int count FROM zora_audit
        WHERE occurred_at>=now()-interval '14 days' GROUP BY 1,2 ORDER BY 1 DESC,3 DESC LIMIT 300`);
      return json(res,200,{activity:rows});
    }
    if (req.method==='GET' && path==='/v1/admin/licenses') return listLicenses(url,res);
    if (req.method==='POST' && path==='/v1/admin/licenses/bulk')
      return json(res,201,{licenses:await createLicenses(await payload(req))});
    if (req.method==='POST' && path==='/v1/admin/licenses/bulk-status') {
      const {ids,status}=await payload(req);
      if (!Array.isArray(ids)||ids.length<1||ids.length>100||ids.some(x=>!uuidPattern.test(x))||!allowedStatuses.has(status))
        throw error(400,'invalid_bulk','Provide 1-100 valid license IDs and a valid status');
      const unique=[...new Set(ids)];
      const c=await db.connect();
      try{await c.query('BEGIN');
        const updated=await c.query('UPDATE zora_licenses SET status=$2 WHERE id=ANY($1::uuid[]) RETURNING id',[unique,status]);
        for(const row of updated.rows) await audit(c,row.id,`license.${status}`,'owner',{bulk:true});
        await c.query('COMMIT');return json(res,200,{updated:updated.rowCount,requested:unique.length,status});
      }catch(e){await c.query('ROLLBACK');throw e;}finally{c.release();}
    }
    if (req.method==='GET' && path==='/v1/admin/audit') {
      const where=[],args=[];
      const action=url.searchParams.get('action');
      if(action){if(action.length>48)throw error(400,'invalid_action','Action too long');args.push('%'+action+'%');where.push(`action ILIKE $${args.length}`);}
      const actor=url.searchParams.get('actor');
      if(actor){if(actor.length>64)throw error(400,'invalid_actor','Actor too long');args.push('%'+actor+'%');where.push(`actor ILIKE $${args.length}`);}
      const licenseId=url.searchParams.get('licenseId');
      if(licenseId){assertUuid(licenseId);args.push(licenseId);where.push(`license_id=$${args.length}`);}
      const hours=url.searchParams.get('hours');
      if(hours){const n=Number(hours);if(!Number.isInteger(n)||n<1||n>8760)throw error(400,'invalid_hours','Hours must be 1-8760');args.push(n);where.push(`occurred_at>now()-($${args.length}::int*interval '1 hour')`);}
      const clause=where.length?'WHERE '+where.join(' AND '):'';
      const limit=limitValue(url.searchParams.get('limit'),100,500),offset=offsetValue(url.searchParams.get('offset'));
      const {rows:count}=await db.query(`SELECT COUNT(*)::int count FROM zora_audit ${clause}`,args);
      const {rows}=await db.query(`SELECT id,occurred_at,license_id,action,actor,details FROM zora_audit ${clause}
        ORDER BY occurred_at DESC,id DESC LIMIT $${args.length+1} OFFSET $${args.length+2}`,[...args,limit,offset]);
      return json(res,200,{events:rows,total:count[0].count,limit,offset});
    }
    const license=path.match(/^\/v1\/admin\/licenses\/([0-9a-f-]{36})(?:\/(.*))?$/i);
    if(license){
      const id=assertUuid(license[1]),sub=license[2]||'';
      if(req.method==='GET' && !sub){
        const {rows}=await db.query(`SELECT id,key_label,username,status,max_devices,created_at,activated_at,expires_at,last_seen_at,note,
          (SELECT COUNT(*)::int FROM zora_devices WHERE license_id=$1) AS devices FROM zora_licenses WHERE id=$1`,[id]);
        if(!rows.length)throw error(404,'not_found','License not found');
        return json(res,200,{license:rows[0]});
      }
      if(req.method==='POST' && sub==='status'){
        const {status}=await payload(req);
        if(!allowedStatuses.has(status))throw error(400,'invalid_status','Invalid status');
        const record=await changeLicense(id,[['status',status]],`license.${status}`);
        return json(res,200,record);
      }
      if(req.method==='POST' && sub==='reset-devices'){
        const result=await transactional(id,'license.devices_reset',async c=>{
          const count=await c.query('DELETE FROM zora_devices WHERE license_id=$1',[id]);
          return {removed:count.rowCount,reset:true};
        });return json(res,200,result);
      }
      if(req.method==='POST' && sub==='clear-username'){
        const result=await transactional(id,'license.username_cleared',async c=>{
          const count=await c.query('DELETE FROM zora_devices WHERE license_id=$1',[id]);
          await c.query('UPDATE zora_licenses SET username=NULL,activated_at=NULL,last_seen_at=NULL WHERE id=$1',[id]);
          return {username:null,removedDevices:count.rowCount};
        });return json(res,200,result);
      }
      if(req.method==='POST' && sub==='extend'){
        const {days}=await payload(req);
        if(!Number.isInteger(days)||days<1||days>3650)throw error(400,'invalid_days','Days must be 1-3650');
        const result=await transactional(id,'license.expiration_extended',async c=>{
          const {rows}=await c.query(`UPDATE zora_licenses
            SET expires_at=GREATEST(COALESCE(expires_at,now()),now())+($2::int*interval '1 day')
            WHERE id=$1 RETURNING id,expires_at`,[id,days]);return rows[0];
        });return json(res,200,result);
      }
      if(req.method==='PATCH' && sub==='update'){
        const input=await payload(req),allowed=['note','maxDevices','expiresAt','username'];
        if(Object.keys(input).length===0||Object.keys(input).some(k=>!allowed.includes(k)))
          throw error(400,'invalid_fields','Unknown or missing fields');
        const changes=[];
        if('note' in input){if(typeof input.note!=='string'||input.note.length>500)throw error(400,'invalid_note','Note too long');changes.push(['note',input.note]);}
        if('maxDevices' in input){if(!Number.isInteger(input.maxDevices)||input.maxDevices<1||input.maxDevices>5)throw error(400,'invalid_devices','Max devices must be 1-5');changes.push(['max_devices',input.maxDevices]);}
        if('expiresAt' in input){
          if(input.expiresAt!==null&&(typeof input.expiresAt!=='string'||!Number.isFinite(Date.parse(input.expiresAt))))throw error(400,'invalid_date','Use ISO date or null');
          changes.push(['expires_at',input.expiresAt===null?null:new Date(input.expiresAt).toISOString()]);
        }
        if('username' in input){if(typeof input.username!=='string'||!/^[A-Za-z0-9_.-]{3,24}$/.test(input.username))throw error(400,'invalid_username','Username invalid');changes.push(['username',input.username.toLowerCase()]);}
        // Username changes invalidate existing device sessions atomically.
        if('username' in input){
          const result=await transactional(id,'license.updated',async c=>{
            const vals=changes.map(x=>x[1]);
            await c.query(`UPDATE zora_licenses SET ${changes.map((x,i)=>`${x[0]}=$${i+2}`).join(',')} WHERE id=$1`,[id,...vals]);
            await c.query('DELETE FROM zora_devices WHERE license_id=$1',[id]);
            const {rows}=await c.query('SELECT id,username,note,max_devices,expires_at FROM zora_licenses WHERE id=$1',[id]);
            return rows[0];
          });return json(res,200,result);
        }
        return json(res,200,await changeLicense(id,changes,'license.updated'));
      }
      if(req.method==='GET' && sub==='devices'){
        const {rows}=await db.query(`SELECT id,created_at,last_seen_at FROM zora_devices WHERE license_id=$1 ORDER BY last_seen_at DESC`,[id]);
        return json(res,200,{devices:rows});
      }
      const match=sub.match(/^devices\/([0-9a-f-]{36})$/i);
      if(req.method==='DELETE' && match){
        const deviceId=assertUuid(match[1]);
        const result=await transactional(id,'license.device_removed',async c=>{
          const removed=await c.query('DELETE FROM zora_devices WHERE id=$1 AND license_id=$2',[deviceId,id]);
          if(!removed.rowCount)throw error(404,'device_missing','Device not found');
          return {removed:removed.rowCount};
        });return json(res,200,result);
      }
    }
    throw error(404,'not_found','Owner command not found');
  };
}
