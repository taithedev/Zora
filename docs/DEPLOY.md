# Zora 0.2 — Neon Function deployment

## Existing Neon project

Neon project **Zora** (`dark-truth-55449043`) uses database **zora** in US East, PostgreSQL 18. Tables `zora_licenses`, `zora_devices`, and `zora_audit` already exist. Do **not** rerun destructive migration SQL. Source: `server/schema.sql`.

## Deployed Neon Function

Neon reports the `zoralicense` function deployment as complete, with invocation URL `https://br-frosty-bar-bbmdnhr7-zoralicense.compute.c-15.us-east-1.aws.neon.tech/`. The private environment contains `ZORA_KEY_PEPPER`, `ZORA_SESSION_SECRET`, `ZORA_ADMIN_TOKEN`, and `ZORA_CLOUD_FUNCTION=1`. Neon injects `DATABASE_URL`. The function code builds through `.github/workflows/neon-function-bundle.yml`. End-to-end login smoke tests remain to be run before public sales.

## Hosting-free activation architecture

The Windows app never talks directly to Postgres. Instead it calls one HTTPS **Neon Function** deployed in the same Neon project: `functions/license/index.mjs`. Neon automatically injects the Postgres connection URL on the function's branch. There is no separately managed API server or activation URL textbox in the app.

The entrypoint imports `server/src/index.mjs` with an HTTP adapter. Bundle using Neon CLI, which includes dependencies (`pg`) and transitive imports:

For subsequent deployments:

1. From the repository root, install Neon CLI and link the existing Zora project/branch.
2. Deploy slug `zoralicense` with entrypoint `functions/license/index.mjs` and Node.js 24; have the function environment define `ZORA_CLOUD_FUNCTION=1`.
3. Store three random **server-only** secrets in the function environment: `ZORA_KEY_PEPPER`, `ZORA_SESSION_SECRET`, and `ZORA_ADMIN_TOKEN`. Each must be at least 24 characters. Never add them to a source file or a distributable EXE. Rotating the pepper invalidates existing license key hashes; treat it as a long-lived secret.
4. Neon injects `DATABASE_URL` automatically. Confirm the public invocation URL `/health` responds with `{ "ok": true }` once deployed. If functions are unavailable in this Neon region/plan, use the existing Vercel serverless entry `api/index.mjs` as a secure alternative; a trusted backend remains necessary.
5. On GitHub repository Settings > Secrets and variables > Actions > Variables, add `ZORA_PUBLIC_LICENSE_URL` set to the **public HTTPS Neon Function invocation URL**. No private database keys go in this variable. Re-run Windows CI to produce an activated build.
6. Use the separate `ZoraAdmin.exe` owner utility only from your private PC. Configure `ZORA_API_URL` to the same invocation URL and `ZORA_ADMIN_TOKEN` to the owner token before running it.

New license keys are shown **once** and cannot later be recovered in plaintext from the database. Owner CLI can view their masked labels, activation state, created dates, ban/revoke status, and audit history. Back up keys securely off-repository. Keep the repository private if source confidentiality matters.

## Verification and selling

Run both Node and Python tests plus Windows CI. Test scanning and recovering owned Unity projects, Unity EXE + Data folders, and APKs from multiple Unity versions; verify imported OBJ/textures and a recovery report in Unity Editor. Paid distribution also needs payment checkout that verifies real payments before key issuance, refunds and owner support. No live checkout has been configured.

Only process content you own or are licensed to extract; Zora does not decrypt third-party DRM/encrypted files. A recovered compiled game is **not** a reproduction of its original Unity Editor project.
