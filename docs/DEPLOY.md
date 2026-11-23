# Zora deployment and licensing checklist

## Neon database

The dedicated Neon project is named **Zora**, with database **zora** (PostgreSQL 18, US East). Its project ID is `dark-truth-55449043`. The schema was created in the database on 2026-11-22; apply `server/schema.sql` only for another database or fresh deployment. There are no license keys until the owner generates them.

## License API

Deploy the repository root as Vercel serverless functions (`api/index.mjs` and `vercel.json`) or deploy `server` to a Node.js 20+ host using HTTPS. The default API uses the Neon connection over TLS. Set these **server-only** secrets:

- `DATABASE_URL` — copy from the Neon console or secret store (never put in desktop files)
- `ZORA_KEY_PEPPER` — independently generated long random secret; changing it permanently invalidates stored key hashes and device IDs
- `ZORA_SESSION_SECRET` — independently generated long random secret; rotation invalidates access tokens (15 minute lifetime)
- `ZORA_ADMIN_TOKEN` — independent long random owner secret; rotate securely if compromised
- `HOST=0.0.0.0` and your provider's `PORT` as needed

Generate each application secret with `node -e "console.log(require('crypto').randomBytes(48).toString('base64url'))"`. Do not log or commit the generated values. Confirm `GET /health` reports ok. Set desktop activation's server URL to your hosted HTTPS API. Do not bind the local HTTP API publicly without HTTPS.

## Owner CLI

In an **owner-only** terminal, set `ZORA_ADMIN_TOKEN` and `ZORA_API_URL=https://your-actual-api-host`, then:

```
node admin/zora-admin.mjs stats
node admin/zora-admin.mjs create 1 1 --output private-keys.json
node admin/zora-admin.mjs create 25 1 --output batch-keys.json
node admin/zora-admin.mjs list
node admin/zora-admin.mjs ban <license-id>
node admin/zora-admin.mjs unban <license-id>
node admin/zora-admin.mjs revoke <license-id>
node admin/zora-admin.mjs reset-devices <license-id>
node admin/zora-admin.mjs audit
```

Raw key files contain full usable licenses. Keep them outside the public repository, transfer to buyers securely, and delete local copies once fulfilled. A lost license can be revoked and replaced; the original key cannot be recovered from its hash.

## Sales / payments

Current price target **$25 USD per lifetime license** (subject to your product terms). No sales service is implemented: do **not** claim automatic $25 payments, receipt validation, chargebacks, refund handling, or webhook issuance work yet. When adding a checkout, use a trusted payment provider's server-side webhook verification, idempotent order records and server-only key issuance. Work with a parent/guardian as needed for payment account eligibility.

## Hardening before selling

- Change the GitHub repository's public/private setting if you want private source distribution.
- Require HTTPS, use a restricted hosting admin/secret environment, and rotate secrets properly.
- Add payment webhooks, order records, purchase receipts, support and a key recovery/refund process.
- Load-test the license API; configure rate limiting at hosting edge (in-memory limiter is per-process only).
- Sign published EXE using a code signing certificate and use installer/release checksum verification.
- Run integration tests against Neon and built Windows artifacts.
- Perform third-party license reviews and compatibility testing with sample projects/games you have permission to use.
- For production, keep the backend **private** and use alerts, backups, audits and monitoring. No DRM solution is unbreakable.
