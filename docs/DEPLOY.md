# Zora v0.3 — password-only Neon owner authentication

## Live Neon architecture

Project: `Zora` (`dark-truth-55449043`) · database `zora` · branch `br-frosty-bar-bbmdnhr7` · function slug `zoralicense`.

Endpoint: `https://br-frosty-bar-bbmdnhr7-zoralicense.compute.c-15.us-east-1.aws.neon.tech/`

The owner Windows executable has this endpoint built in. **It asks only for the owner password.** It does not need a database URL, username, HTTP admin token, or URL input field. The URL is public, but the SQL credentials and password hashes are private to Neon.

### Database migration (applied)

`server/migration-owner.sql` creates `zora_owner_auth` and `zora_owner_sessions` without removing or changing existing license records. The owner password is salted and scrypt-hashed; plaintext passwords are never stored.

### Set the initial owner password (required)

1. In Neon Console, open **Zora** and copy its Postgres connection string securely for your own terminal. **Never** upload it to GitHub or distribute it in ZoraAdmin.exe.
2. On the administrator PC, install Node.js 22+ and run `cd server && npm install`.
3. Set the `DATABASE_URL` environment variable privately in that terminal. Do not paste the URL into chat or a committed `.env`.
4. Run `node scripts/setup-owner.mjs`. It requests the password **twice with no terminal echo**, hashes it using scrypt, upserts it into `zora_owner_auth`, and revokes older owner sessions. The same script can rotate a forgotten password using your private Neon database access.
5. If you have a private initial password file from the project setup, enter its password at this prompt. Otherwise choose a unique password at least 16 characters long. Do not store or reuse it in public files.

The initial owner password has not been seeded automatically. **Password sign-in won't work until this step finishes.** No app authentication token bypass is provided.

### Deploy the updated Neon Function (required)

Neon Functions package entry: `functions/license/index.mjs`, which uses `server/src/index.mjs` and `server/src/owner.mjs`.

- Bundle using GitHub workflow `.github/workflows/neon-function-bundle.yml` or equivalent esbuild command. Ensure the bundle includes the `pg` dependency and emits `index.mjs` in a ZIP.
- Deploy the ZIP to the existing Neon Function slug `zoralicense`, with Node.js 24, `ZORA_CLOUD_FUNCTION=1`, `ZORA_KEY_PEPPER`, and `ZORA_SESSION_SECRET` (existing deployment secrets). Neon injects `DATABASE_URL`. Preserve the pepper or existing license keys will no longer match.
- `ZORA_ADMIN_TOKEN` can be removed from the function environment; it is ignored by v0.3.
- Verify `/health` returns an `ok: true` response. Then test owner password sign-in and `/v1/admin/stats` with the owner console.
- Ensure Windows CI includes the existing `ZORA_PUBLIC_LICENSE_URL` GitHub Actions variable for normal client activation.

No separate Node server or paid license server process is required in production. The managed Neon Function is the safe boundary between native clients and Postgres.

### Owner console features

Use `ZoraAdmin.exe`. Enter your owner password and type `help` to view the 49 menu choices. Supported operations include license creation, editing, searching, filtering, banning, unbanning, revoking, device management, expiration, username binding, audit filtering and exports, owner session control, password rotation, and health checks.

- Bulk-generated keys are only shown once and are saved locally to a **new** JSON file. Keep exports encrypted or in private storage; they contain sensitive license metadata.
- `Change owner password` revokes all active owner sessions; log in again with the new password.
- Five incorrect passwords trigger a ten-minute database lockout. Contact Neon through your owner account for recovery, not customer support channels.
- CSV files contain masked license identifiers, never complete license key material.

### Verification before public sales

Run server unit tests, Windows build, and live owner login tests. Confirm old API admin-token access fails, owner password rotation revokes sessions, and banned keys cannot refresh. Test each Unity EXE/APK importer and recovery outputs against owned Unity projects. A $25 payment checkout that verifies successful transactions server-side has **not** been integrated; do not treat generated keys as paid purchases.
