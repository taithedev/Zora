using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Zora.Updates;

public sealed record StagedUpdate(Version Version, string ZipPath, string Sha256, string Tag);

/// <summary>Checks public GitHub releases, verifies their SHA-256 digests and stages portable updates.</summary>
public sealed class UpdateService(string assetName, string launchExecutable)
{
    private const string ReleasesUrl = "https://api.github.com/repos/taithedev/Zora/releases/latest";
    private const string ReleasePath = "https://github.com/taithedev/Zora/releases/download/";
    private const long MaxDownloadBytes = 600L * 1024 * 1024;
    private static readonly HttpClient Http = CreateClient();
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ZoraUpdater", "0.4"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);

    public async Task<StagedUpdate?> CheckAndStageAsync(CancellationToken cancellationToken = default)
    {
        using var feed = await Http.GetAsync(ReleasesUrl, cancellationToken);
        if (!feed.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub release check failed (HTTP {(int)feed.StatusCode}).");
        await using var feedContent = await feed.Content.ReadAsStreamAsync(cancellationToken);
        using var release = await JsonDocument.ParseAsync(feedContent, cancellationToken: cancellationToken);
        var root = release.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version)) return null;
        if (version <= CurrentVersion) return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != assetName) continue;
            var uriText = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
                url.Host != "github.com" || !url.AbsoluteUri.StartsWith(ReleasePath, StringComparison.Ordinal))
                throw new InvalidOperationException("The release asset URL is not an official Zora GitHub release.");
            if (!asset.TryGetProperty("digest", out var digestItem))
                throw new InvalidOperationException("This release has no SHA-256 digest. Update not installed.");
            var digest = digestItem.GetString() ?? "";
            if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
                digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit))
                throw new InvalidOperationException("This release has no valid SHA-256 digest.");
            var expected = digest[7..].ToUpperInvariant();
            var basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                        "Zora", "updates", tag);
            Directory.CreateDirectory(basePath);
            var final = Path.Combine(basePath, assetName);
            if (File.Exists(final) && await VerifyAsync(final, expected, cancellationToken))
                return new StagedUpdate(version, final, expected, tag);
            var partial = Path.Combine(basePath, $"{assetName}.{Guid.NewGuid():N}.partial");
            try
            {
                using var download = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                download.EnsureSuccessStatusCode();
                if (download.Content.Headers.ContentLength > MaxDownloadBytes)
                    throw new InvalidOperationException("The update archive exceeds the 600 MB size limit.");
                await using var source = await download.Content.ReadAsStreamAsync(cancellationToken);
                await using var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, true);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[1024 * 128];
                long total = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    total += read;
                    if (total > MaxDownloadBytes) throw new InvalidOperationException("Update download exceeded 600 MB.");
                    sha.AppendData(buffer.AsSpan(0, read));
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                await file.FlushAsync(cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(sha.GetHashAndReset(), Convert.FromHexString(expected)))
                    throw new InvalidOperationException("Update SHA-256 verification failed. No files were modified.");
                File.Move(partial, final, true);
                return new StagedUpdate(version, final, expected, tag);
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
        return null;
    }

    private static async Task<bool> VerifyAsync(string path, string expected, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(expected));
    }

    public void ApplyAndRestart(StagedUpdate update)
    {
        if (!File.Exists(update.ZipPath)) throw new FileNotFoundException("Staged update missing", update.ZipPath);
        var install = Path.GetFullPath(AppContext.BaseDirectory);
        if (!File.Exists(Path.Combine(install, launchExecutable)))
            throw new InvalidOperationException("Zora is not running from a portable installation.");
        // Do not silently run elevated or replace files inside protected folders.
        var writableTest = Path.Combine(install, $".zora-writable-{Guid.NewGuid():N}");
        try { File.WriteAllText(writableTest, "test"); }
        catch (UnauthorizedAccessException) { throw new InvalidOperationException("This install directory is read-only. Install Zora under your user directory to enable automatic updates."); }
        finally { if (File.Exists(writableTest)) File.Delete(writableTest); }
        var updater = Path.Combine(install, "ZoraUpdater.exe");
        if (!File.Exists(updater)) throw new FileNotFoundException("The release does not include ZoraUpdater.exe", updater);
        var standalone = Path.Combine(Path.GetTempPath(), $"ZoraUpdater-{Guid.NewGuid():N}.exe");
        File.Copy(updater, standalone);
        var process = new ProcessStartInfo(standalone) { UseShellExecute = false, WorkingDirectory = install };
        foreach (var argument in new[] { "--archive", update.ZipPath, "--sha256", update.Sha256, "--target", install,
                                         "--pid", Environment.ProcessId.ToString(), "--launch", launchExecutable })
            process.ArgumentList.Add(argument);
        if (Process.Start(process) is null) throw new InvalidOperationException("Failed to start the update installer.");
    }
}
