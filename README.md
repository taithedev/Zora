# ZORA — Native Unity Recovery Studio

**Version:** 0.3.0 developer preview · **Planned price:** $25 per license · **Windows 10/11 x64** · Native .NET 8 WPF and native Windows owner CLI (no Electron).

Zora has a strict black-and-white, square-cornered OLED desktop design. The client launches into a centered license-key entry, then requests a username, and opens its asset inspection workspace only after activation. The public application contains neither database credentials nor owner credentials.

## Asset import and recovery

- Open complete Unity project directories with `Assets` and `ProjectSettings`. Copy source `.cs`, `.meta`, scenes, prefabs, and matching settings if available.
- Open compiled Unity Windows `.exe` games with their nearby `<Game>_Data` folder, Unity `.apk` files, and supported asset bundles.
- Search, filter, preview and export UnityPy-compatible textures/sprites (PNG), meshes (OBJ), audio samples (WAV), text assets, fonts, and supported JSON metadata.
- Best-effort **Recover Unity project** output: Unity Hub-importable folder scaffold with recoverable assets, `ProjectSettings`, `Packages`, and `RECOVERY_REPORT.json`.

**Important:** Compiled builds do not contain a complete original Unity Editor project. Zora cannot guarantee C# source, prefab hierarchies, full scene reconstruction, materials/shaders, meshes/rigs, or any protected/encrypted assets. Do not use it to bypass DRM, and only work with assets you own or have permission to extract.

## Owner Console — 49 commands, one password

`ZoraAdmin.exe` displays only `Owner password:` on first launch. No username, no API address, no owner API token, no Neon connection string. The password is **stored in Neon only as a salted scrypt hash**; it is verified over HTTPS by the deployed Neon Function. After login, it grants an 8-hour revocable owner session held only in app memory.

The owner console includes these 49 commands:

1. Overview; 2. All licenses; 3. Search; 4. License details; 5. Generate one; 6. Generate bulk (up to 500); 7. Active; 8. Banned; 9. Revoked; 10. Unactivated; 11. Activated; 12. Inactive 30+ days; 13. Expiring 30 days; 14. Expired; 15. Created in 24h; 16. Activated in 24h.
2. Ban, unban, revoke; reset devices; change max devices; edit/clear notes; set/clear/extend expiration; rename or clear usernames; inspect/remove devices; bulk status updates (up to 100); audit history and audit filters; daily activity breakdown.
3. Paginated CSV and JSON exports for licenses and audit logs; owner security status; live sessions; revoke other/all owner sessions; change owner password; Neon health check; logout; re-login; help and exit.

All commands are implemented against server-side Neon SQL endpoints. The server never returns plaintext license keys after their initial generation; only masked labels, metadata, devices, and audit records. Generated key batches are saved to a **new local private file**; the console avoids overwriting existing files. Each licensing action is recorded in the audit trail. License counts × $25 are *not* verified revenue.

## Why a Neon Function is still required

The owner EXE does **not** directly connect to Neon Postgres: shipping database credentials inside any distributed EXE would expose every paid license and customer. The HTTPS endpoint runs **inside Neon** as a Neon Function, not as a separate license server process. Owner passwords and session hashes live in the `zora` database; all SQL is performed inside the managed function.

- Neon project: `Zora` / database `zora` / Postgres 18.
- Function slug: `zoralicense`.
- The old `ZORA_ADMIN_TOKEN` is **not accepted** for owner access as of v0.3.
- App users still sign in with their license key and a username; the owner signs in with **only their owner password**.
- Password login has salted scrypt hashing, failed-login lockout, owner-session expiry and revocation, password rotation, and an audit trail.

**Deployment status:** The Neon schema for owner auth has been applied, but the owner password must still be initialized and this new function version must be deployed and smoke-tested before password login works live. See [`docs/DEPLOY.md`](docs/DEPLOY.md). Never put the owner password, database URL, secret keys, or server tokens into GitHub.

## Build and testing

Run `cd server && npm test` for password/crypto tests, `python -m unittest discover -s engine/tests -v` for recovery tests, and the GitHub Actions workflow for Windows builds. The native owner console builds from `admin/Zora.Admin/` and is independent of the desktop client.

Payment checkout is **not** integrated. No real revenue is recorded by license generation, and this is not yet a verified commercial release. Upstream UnityPy is MIT licensed; AssetRipper GPL source is not included in Zora.
