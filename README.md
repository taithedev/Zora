# ZORA — Native Application & Unity Asset Studio

**Version:** 0.5.0 developer preview · **Target price:** $25 per license · **Platform:** Windows 10/11 x64 · Native .NET 8 WPF, rounded monochrome OLED theme. No Electron.

Zora starts with the centered license-key screen, then requests a username. All licensing and owner actions run against the deployed Neon Function; clients never receive Neon database credentials.

## New in v0.5 — Zuro & Code Studio

- **Refreshed monochrome interface:** rounded panels and buttons, Windows 11 rounded window treatment, animated button hover/press, and eased page transitions. All UI colors remain white, black, and grayscale.
- **Code Studio:** integrated tabbed text/source editor using MIT-licensed [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) `6.3.1.120`, with line numbers, monochrome syntax highlighting, open/new/save/save-as, unsaved-change prompts, search, go to line, Ctrl+D duplicate line, Ctrl+Tab switching, Tab-to-complete keyword suggestions, Ctrl+Space suggestions, and indentation. Double-click a text asset inside an *authorized original Unity project* to open it.
- **Zuro:** owner workspace inside the same EXE, gated by the existing Neon owner-password API. Shows totals, activated users, bans, device counts, recent license records and real bulk license generation (1–500 keys), plus ban/unban/revoke actions. Owner sessions live in memory only and are cleared on logout; raw generated keys are shown once. The public-facing license does not grant admin permissions.
- **Guardrails:** Code Studio only opens local source/text files up to 5 MB; it does not compile, run, patch or decrypt EXEs. Zuro never embeds the Neon database password or owner password into the executable.

**Limitations:** Code Studio has keyword completion, not full Roslyn IntelliSense, debugger, or dnSpy-style assembly decompilation. Compiled Unity builds may contain scripts only as binaries and cannot be restored to original C# project files without loss. The v0.5 Windows build must pass CI before publication.

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
- Release publishing: push a tested version tag such as `v0.4.0` to trigger the workflow and publish both portable ZIPs to GitHub Releases. Future versions with higher semantic versions can update from that feed.
- To run locally: `python -m pip install -r engine/requirements.txt`, `cd server && npm test`, `python -m unittest discover -s engine/tests -v`, and `dotnet run --project desktop/Zora.Desktop` on Windows with the .NET 8 SDK.

See [deployment details](docs/DEPLOY.md). The project is still a developer preview, so automated tests cannot substitute for testing with actual EXEs/APKs and Windows installation locations.

UnityPy and pefile have their own upstream licenses. No GPL AssetRipper code was copied into this repository.


## Discord sync (Zuzify bot integration)

- **Zora → Discord sync** presents a browser-based Discord OAuth2 authorization flow (`identify` only). Verified Discord ID, username and display name are associated with the activated Zora license in Neon. No Discord passwords, access tokens, emails or message histories are stored.
- The user may unlink at any time. Only one Zora license per Discord user is permitted; only active license holders can start linking.
- The Zuzify Discord bot has a private `/zora` command that queries Zora's Neon function with a dedicated service secret. The bot never receives the user's Zora license key.
- Database tables are deployed in the Zora Neon `zora` database. See [Discord setup](docs/DISCORD.md) to configure the OAuth2 client secret and matching redirect URI. Until the environment variables are configured and the function redeployed, the Connect button will report that linking is unavailable.
- Zora has the latest monochrome dropdown styling to avoid system-white filter lists on Windows.
