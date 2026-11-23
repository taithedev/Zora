# Zora portable update process

The desktop and console use the *published GitHub Releases* feed (not GitHub workflow artifacts). Their downloader requires the matching ZIP asset, a newer `vMAJOR.MINOR.PATCH` release tag, HTTPS GitHub download URL, and a valid SHA-256 `digest` provided by the GitHub API. If any requirement fails, **no update is installed**.

The update is downloaded to `%LOCALAPPDATA%\\Zora\\updates\\<version>` (no API keys or credentials). A separate updater process validates SHA-256 again, waits until the app exits, rejects unsafe ZIP entries, extracts into a private temporary folder, copies files into the existing portable installation, then restarts Zora. Zora does not silently elevate privileges. The updater cannot update a non-writable installation directory; install the portable build in a user-writable folder.

To publish:

1. Push source changes to `main`, and verify `Zora Windows build` completed successfully.
2. For this initial release, push a reviewed `release: v0.4.0` commit to `main`, or create and push Git tag `v0.4.0`. GitHub Actions builds portable `Zora-Windows-x64.zip` and `ZoraAdmin-Windows-x64.zip`, including `ZoraUpdater.exe`, and publishes a Release if both build/test jobs pass.
3. Verify the Release exposes GitHub API `digest: sha256:...` metadata on both ZIP assets.
4. Test upgrading from an older portable folder in Windows without admin privileges and test its rollback/restore strategy before production rollout.

Current updater is *not atomic* and does not implement rollback if copying files fails partway. Keep the previous ZIP available. Avoid modifying files while the app is running. No unsigned external update script is downloaded or executed.
