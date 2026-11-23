# Zora Windows installer (v0.7.0)

Download `Zora-Setup-x64.exe` from [official Zora releases](https://github.com/taithedev/Zora/releases).

1. Double-click the setup EXE.
2. The per-user installer installs Zora into `%LOCALAPPDATA%\Programs\Zora`; no administrator access, Python installation, Node installation, or separate .NET runtime required.
3. Zora creates a Start Menu shortcut, offers an optional desktop icon, registers an uninstaller under Windows Installed Apps, and launches when setup finishes.
4. Enter your existing license key if this is a new install. Existing activations are stored in `%LOCALAPPDATA%\Zora\settings.json` and survive upgrading or uninstalling.
5. The bundled `engine\zora-engine.exe` is installed alongside `Zora.exe` and `ZoraUpdater.exe`.

## Updates

When a new published GitHub Release becomes available, Zora checks the official SHA-256 digest. The **installed** edition downloads the updated `Zora-Setup-x64.exe` and uses a separate updater helper to close Zora, run silent per-user upgrade, then relaunch. Portable ZIP installations continue to download and unpack the portable ZIP, without touching Windows installed-app registration. Zora requires confirmation before the update restarts it. Existing unsaved editor changes should be saved first.

## Developer build

The Windows GitHub Actions workflow publishes a self-contained WPF app, the bundled Python extraction engine, and the Inno Setup installer. It performs a silent install / file-verification / uninstall smoke test. Installer creation requires Inno Setup 6 (preinstalled on GitHub-hosted Windows runners).

```powershell
dotnet publish desktop/Zora.Desktop/Zora.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
# Copy the bundled engine/zora-engine.exe and ZoraUpdater.exe into publish first.
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" installer\Zora.iss
```

Do not distribute the owner admin password, database credentials, or sensitive API keys in the installer. Unsigned installers can trigger Microsoft SmartScreen; production code-signing is recommended. Always test releases on a real Windows desktop.
