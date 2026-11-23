# Zora installer and portable release process

Starting in v0.7.0, the official release provides **Zora-Setup-x64.exe** (one-click per-user Windows installer), **Zora-Windows-x64.zip** (portable build), and **ZoraAdmin-Windows-x64.zip** (owner-only console).

- Installer contains the .NET self-contained executable, engine, update helper, shortcuts, Installed Apps entry and uninstaller. It launches Zora after interactive setup.
- Installed copies contain a `.zora-installed` marker. When an update is available, the update helper downloads the installer instead of the ZIP, validates the official GitHub asset SHA-256 and restarts via silent installer upgrade after Zora exits.
- Portable and owner-console builds continue using verified ZIP updates. The existing user profile and Neon account activation persist across updates.
- CI compiles with Inno Setup 6, performs unattended install and uninstall tests, and publishes the setup EXE alongside portable ZIPs. Do not release unless the Windows and Node test jobs pass.
- No administrator rights, Node, Python, or .NET runtime installs are needed for users.

The update mechanism verifies the SHA-256 digest returned by GitHub Releases, but this is not equivalent to a signed publisher identity. Consider trusted code-signing before commercial distribution.

## Publishing

Push a reviewed `release: v0.7.0` commit, or push a new `v0.7.0` tag. The GitHub Actions workflow generates and uploads all three distributables to a published GitHub Release. Later releases must update the assembly and installer versions together. See [INSTALLER.md](INSTALLER.md).
