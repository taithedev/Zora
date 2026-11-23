# ZORA
### A native OLED Unity asset workspace · Windows · Paid license: **$25 USD**

Zora is an independently developed desktop asset inspection/export app for Unity project folders and authorized compiled builds. Its interface is native C# / WPF (no Electron). It uses a bundled Python extractor based on [UnityPy](https://github.com/K0lb3/UnityPy) (MIT license). The licensing API runs on Node.js + Neon PostgreSQL; there is a separate owner-only command-line license console.

> **Status: developer preview, not a finished commercial release.** Engine integrations and Windows builds must be validated against multiple real Unity versions. There is no payment checkout or hosted license endpoint yet. The public repository contains source code; keep production secrets in hosting environment variables, not in this repository.

## Current feature scope

- Load a Unity project folder with `Assets` + `ProjectSettings`, browse/copy original assets (FBX, OBJ, PNG, scripts, prefabs, scenes, etc.)
- Load compiled Unity game `Game.exe` with nearby `Game_Data`, Unity APKs, `.assets`, `.bundle`, `.unity3d` and similar serialized files
- Search, type filters, item metadata, multi-selection, native texture preview and on-demand serialized texture preview
- Export decoded `Texture2D` / `Sprite` to PNG, `Mesh` to OBJ, `AudioClip` to WAV when decodable, fonts to TTF/OTF, `TextAsset` to raw text
- Export readable `MonoBehaviour`, `Material`, `AnimationClip`, `Shader` metadata as JSON when type trees are available. **Not** full prefab/scene reconstruction, FBX rig reconstruction, or 3D scene rendering
- License-bound username accounts, one activation device by default, online verification, secure random license keys, revocation, audit log
- Owner CLI: bulk keys (up to 500), ban, unban, revoke, reset device activations, audit, stats, account list

**Limitations:** This app does not decrypt protected content, defeat DRM, or reconstruct entire Unity source projects from arbitrary games. Use only assets you own or have permission to extract. Zora cannot claim better feature coverage than AssetRipper at this stage.

## Structure

```
admin/                    Owner-only key management console
server/                   Neon-backed license API and database migration
engine/                   UnityPy asset index + export engine (Python)
desktop/Zora.Desktop/      Native .NET 8 Windows WPF interface
.github/workflows/        Windows build and licensing tests
```

## Local developer run

1. Install **.NET 8 SDK**, **Node.js 20+**, and **Python 3.11** on Windows.
2. Create the `Zora` Neon project (already created for this deployment) and run `server/schema.sql` on the `zora` database if deploying elsewhere.
3. In `server`, `npm install`. Set `DATABASE_URL`, `ZORA_KEY_PEPPER`, `ZORA_SESSION_SECRET`, `ZORA_ADMIN_TOKEN`, then `npm start`. See `server/.env.example` and `docs/DEPLOY.md`. Only the API server gets database credentials.
4. Set `ZORA_API_URL=http://127.0.0.1:8787` and `ZORA_ADMIN_TOKEN` in your terminal. Run `node admin/zora-admin.mjs create 1` to generate your first key. **Keys are displayed only once.**
5. `python -m pip install -r engine/requirements.txt`; then `dotnet run --project desktop/Zora.Desktop`. Enter a username, the generated key, and the local API URL in the native app.
6. Select an owned Unity project or build and export supported assets to a separate folder.

The app calls the local Python script in a developer checkout. GitHub Actions packages it into `engine/zora-engine.exe` and builds a self-contained Windows release ZIP. API hosting must be configured separately.

## Price and commercial protection

**Price target: $25/license.** `zora-admin create 1` generates a license; **it does not collect payment**. A later checkout must verify successful payment *server-side* before granting a key. The license API stores only HMAC hashes of keys and one-way hashes of refresh tokens. The desktop client stores its refresh token using Windows DPAPI. APIs check license and device status before continuing. No desktop app can be made impossible to crack; signing releases, private backend secrets, monitoring and defense-in-depth help.

Do **not** publish the owner's `ZORA_ADMIN_TOKEN`, Neon `DATABASE_URL`, `ZORA_KEY_PEPPER` or `ZORA_SESSION_SECRET`. The GitHub repository is currently **public**; make it private before relying on source secrecy for a commercial release.

## Third-party licenses

UnityPy: MIT, copyright respective contributors; see its upstream LICENSE. UnityPy's README requests commercial users consider sponsorship or charity donations. AssetRipper is GPL-3.0; **no AssetRipper implementation code has been copied into Zora**. Do not include it in a proprietary binary without evaluating GPL obligations.

## Tests

`cd server && npm test` runs crypto/key tests. The GitHub Actions workflow builds the Windows desktop and extraction engine on a Windows runner; this Linux authoring environment cannot compile WPF. Test with Unity samples before accepting real payments.
