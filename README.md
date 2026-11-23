# ZORA — Native Application & Unity Asset Studio

**Version:** 0.4.0 developer preview · **Target price:** $25 per license · **Platform:** Windows 10/11 x64 · Native .NET 8 WPF, black-and-white square-cornered OLED theme. No Electron.

Zora starts with the centered license-key screen, then requests a username. All licensing and owner actions run against the deployed Neon Function; clients never receive Neon database credentials.

## New in v0.4

- **Automatic release updates:** Once signed in, Zora checks the latest *published* GitHub Release, downloads a newer Zora-Windows-x64.zip, verifies its GitHub-reported SHA-256 digest and offers to restart to install. Also available from the sidebar. Files are only replaced after Zora exits; the separate native ZoraUpdater.exe performs the update. It never bypasses Windows permissions or requires hidden elevation.
- **Owner console update + live sync:** ZoraAdmin.exe checks its own newer signed release when opened and offers restart/update; command 50 refreshes live Neon health and license statistics, and command 51 manually checks updates.
- **Reliable real license generation:** The owner console generates one or up to 500 keys through the existing authenticated Neon endpoint, confirms key format, saves one-time plaintext keys into a *new private JSON file*, and displays a single new key for copying. Password-only owner login and revocable sessions remain in place. Keys are real database records, not random strings that the desktop app cannot activate.
- **Broader file support:** Read unencrypted APK/AAB/JAR/ZIP/MSIX/NuGet archives, inspect Windows EXE/DLL/OCX/SYS PE headers and sections, read optional PE imports/exports/resource tables, export ordinary PE resources and ZIP-contained files, and inspect Unity projects, Unity EXEs, Unity APKs and Unity bundles.
- **Safer exports:** Reject archive path traversal, symlink entries, encrypted ZIP entries and oversized extraction; never overwrite existing files when extracting ordinary archives.
- **Unified workspace:** The same asset explorer, filters and selection/export UI work for Unity assets, ZIP contents and Windows application resources.

**Important distinction:** This is a read-only inspection and asset/resource-export tool, not a DRM or license cracker. It does not bypass paid-application activation, encryption, anti-tamper or access controls; compiled code generally cannot be reconstructed into the original source project. Use it with applications and assets you own or are authorized to inspect.

## Usage

1. Launch `Zora.exe` alongside `engine/zora-engine.exe` and `ZoraUpdater.exe`; paste your activated Zora license key and choose a username.
2. Drop a supported source or choose **Open app / archive**. Unity project folders can also be opened directly.
3. Filter and search. **Export selected** or **Export all** saves individual readable assets and ordinary application resources to a chosen output directory.
4. **Recover Unity project** is only valid for a Unity project folder or a recognizable compiled Unity build. It produces an editable scaffold and `RECOVERY_REPORT.json`, never a guaranteed copy of the original source.
5. **Check for updates** is automatic after activation, or manual from the sidebar. Only `v*` tag GitHub Releases published with `Zora-Windows-x64.zip` and its SHA-256 `digest` are installable; GitHub Actions on `main` alone does not create a release.

## Owner console

`ZoraAdmin.exe` asks only for the owner password, which is verified via Neon. It includes 49 previous controls (license issuing, bans, activation limits, usernames, expirations, exports, audit logs, owner security) plus **50 Sync Neon** and **51 Check / install update**. The owner app ships with `ZoraUpdater.exe` beside it. For one license, the new key is displayed; for bulk, keys are saved in private JSON. The password and API credentials are not embedded into the binaries.

> A license grant is not a payment receipt. Checkout and revenue verification are still to be built. Changing the server-side HMAC pepper invalidates previous licenses; **never rotate it during an ordinary Zora update.**

## Deployment and releases

- Neon database: project `Zora`, PostgreSQL 18, database `zora`.
- Managed HTTPS function: slug `zoralicense` (the existing one; no new independent license server).
- Build and unit tests: `.github/workflows/windows-build.yml` runs Node tests, Python tests, and publishes Windows app, updater and console.
- Release publishing: push a tested version tag such as `v0.4.0`, or a reviewed `release: v0.4.0` commit to `main`, to trigger the workflow and publish both portable ZIPs to GitHub Releases. Future versions with higher semantic versions can update from that feed.
- To run locally: `python -m pip install -r engine/requirements.txt`, `cd server && npm test`, `python -m unittest discover -s engine/tests -v`, and `dotnet run --project desktop/Zora.Desktop` on Windows with the .NET 8 SDK.

See [deployment details](docs/DEPLOY.md). The project is still a developer preview, so automated tests cannot substitute for testing with actual EXEs/APKs and Windows installation locations.

UnityPy and pefile have their own upstream licenses. No GPL AssetRipper code was copied into this repository.
