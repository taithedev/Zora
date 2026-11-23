# ZORA — Native Unity Recovery Studio

**Version:** 0.2.0 developer preview | **Price target:** $25 USD / license | **Platform:** Windows 10/11 x64, .NET 8 WPF (native, no Electron).

A strict black/white OLED desktop app with square controls, no ornamental outlines, no secondary activation popup, and a single workspace. The app first displays a centered **license key** input, then a **username** input. Valid activation opens the full workspace; subsequent sessions can resume using a protected refresh token. No database credentials, administrator keys, or API address are shown in the UI.

## Functionality

- Open original Unity project directories with `Assets` and `ProjectSettings`; preserve `.meta`, source `.cs`, `.unity` scenes, `.prefab` files, and settings when recovering projects you own.
- Inspect **Unity Windows EXE** games (requires the nearby `<Game>_Data` folder), **APK** files, or serialized Unity assets with UnityPy.
- Index, filter, preview texture assets, export supported textures/sprites as PNG, meshes as OBJ, audio as WAV where decodable, fonts, readable TextAssets, and JSON metadata from supported serialized types.
- **Recover Unity project**: create a Unity Hub-importable project scaffold from a compiled game with the recovered assets plus `ProjectSettings`, a `Packages/manifest.json`, and a `RECOVERY_REPORT.json` recording limitations. Recover an original project by copying its actual Unity sources and `.meta` files.
- Multi-selection exports, searchable asset grid, drag and drop, in-app status/warnings, one-window application with custom title bar.
- Licensing in dedicated Neon **Zora** project: HMAC key hashes, per-license usernames, device activation, refresh sessions, bans, revocation, audit trail, and owner CLI key management (including bulk generation).

**Critical limitation:** A compiled EXE/APK does not contain an intact Unity Editor project. Zora cannot guarantee restoration of original source C#, scenes, prefab reference hierarchies, shader source, or imported mesh rigs. The recovery option produces a *best-effort importable project*, **not** the original project. This implementation does not bypass encryption, access restrictions, or DRM. Use only content you own or are permitted to extract.

## Activation security

Zora uses a **Neon Function** for secure activation and database queries. This is not a separately hosted license-server process; the function runs inside the Neon project and exposes a public HTTPS URL. Keeping all secret signing keys and database credentials in this trusted runtime is necessary: a distributable desktop EXE cannot safely connect to Postgres using privileged credentials. The desktop receives only a short-lived signed access token plus a refresh token protected by Windows DPAPI. No native DRM is unbreakable.

- The Neon project and the licensing tables already exist.
- **The Neon Function has been deployed to the Zora project with server-only secrets.** Its active deployment is reported complete by Neon. Live desktop activation and end-to-end HTTPS connectivity still need smoke testing.
- The repository includes source for the Neon Function in `functions/license/index.mjs` and server-side licensing logic under `server/` (reused inside the function, not a Windows GUI or separate host).
- The desktop source embeds the public function URL `https://br-frosty-bar-bbmdnhr7-zoralicense.compute.c-15.us-east-1.aws.neon.tech/`. The repository Actions variable `ZORA_PUBLIC_LICENSE_URL` may override the URL at build time; in local developer runs, environment variable `ZORA_API_URL` can override it for testing. No secrets are in the EXE.

## Running and packaging

Read `docs/DEPLOY.md` to deploy the Neon Function, set its secrets, generate the first keys, and configure CI. GitHub Actions packages the native app with its UnityPy extraction engine into one Zora distribution ZIP; `ZoraAdmin.exe` is a separate owner-only console utility.

The CLI can issue keys, but **payment checkout is not implemented**. Do not distribute a paid public release or claim the product is fully licensed until server-side $25 purchase confirmation and refunds are implemented and the release is tested against real Unity 2020–2026 examples.

## Build tests

- `python -m unittest discover -s engine/tests -v`
- `cd server && npm test`
- Windows GitHub Actions publishes `Zora-Windows-x64.zip` and `ZoraAdmin-Windows-x64.zip` after a successful CI run.

## Third-party code

UnityPy is MIT-licensed (upstream copyright and license notices apply). AssetRipper is GPL-3.0; no AssetRipper code is copied into this proprietary project. The product is not affiliated with Unity Technologies. Confirm third-party license obligations before selling.
