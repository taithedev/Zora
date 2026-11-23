// Neon Functions HTTPS entrypoint. Neon injects DATABASE_URL; all other
// secret values are deployment environment variables, never embedded in Zora.exe.
// Build with the Neon CLI (esbuild bundles pg and its dependencies).
import nodeHandler from '../../server/src/index.mjs';

export default {
  async fetch(request) {
    const uri = new URL(request.url);
    const headers = Object.fromEntries(request.headers.entries());
    // The Node handler expects req.body/res.writeHead; this adapter only
    // supplies those members. No app server or open listening port is needed.
    let body = undefined;
    if (request.method !== 'GET' && request.method !== 'HEAD') {
      const size = Number(request.headers.get('content-length') || 0);
      if (size > 64000) return Response.json({ error: 'too_large', message: 'Request body too large' }, { status: 413 });
      const raw = await request.text();
      if (raw.length > 64000) return Response.json({ error: 'too_large', message: 'Request body too large' }, { status: 413 });
      try { body = raw.trim() ? JSON.parse(raw) : {}; }
      catch { return Response.json({ error: 'invalid_json', message: 'Invalid JSON' }, { status: 400 }); }
    }
    const req = { method: request.method, url: uri.pathname + uri.search, headers, body,
      socket: { remoteAddress: request.headers.get('x-real-ip') || request.headers.get('cf-connecting-ip') || request.headers.get('x-forwarded-for')?.split(',')[0]?.trim() || 'unknown' } };
    let code = 500, replyHeaders = {}, data = '';
    const res = {
      writeHead(status, h) { code = status; replyHeaders = h; },
      end(chunk) { data = chunk ?? ''; }
    };
    await nodeHandler(req, res);
    return new Response(data, { status: code, headers: replyHeaders });
  }
};
