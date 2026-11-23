using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Zora.Updater;

public static class Program
{
    private const long MaxUnpackedBytes = 3L * 1024 * 1024 * 1024;
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i + 1 < args.Length; i += 2) parameters[args[i]] = args[i + 1];
            var archive = Path.GetFullPath(parameters["--archive"]);
            var target = Path.GetFullPath(parameters["--target"]);
            var launch = parameters["--launch"];
            var digest = parameters["--sha256"];
            var pid = int.Parse(parameters["--pid"]);
            if (!new[] { "Zora.exe", "ZoraAdmin.exe" }.Contains(launch, StringComparer.Ordinal) ||
                !Directory.Exists(target) || !File.Exists(Path.Combine(target, launch)) ||
                digest.Length != 64 || !digest.All(Uri.IsHexDigit))
                throw new InvalidOperationException("Invalid update destination or digest.");
            if (pid <= 0) throw new InvalidOperationException("Invalid process ID.");
            await using var verifyStream = File.OpenRead(archive);
            var hash = await SHA256.HashDataAsync(verifyStream);
            if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(digest)))
                throw new InvalidOperationException("Downloaded update checksum failed.");
            try
            {
                using var previous = Process.GetProcessById(pid);
                if (!previous.WaitForExit(30_000)) throw new TimeoutException("Zora did not exit before update.");
            }
            catch (ArgumentException) { /* The application exited before the updater started. */ }
            var staged = Path.Combine(Path.GetTempPath(), $"zora-staged-{Guid.NewGuid():N}");
            Directory.CreateDirectory(staged);
            try
            {
                using (var zip = ZipFile.OpenRead(archive))
                {
                    long total = 0;
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.Name.Length == 0) continue;
                        var filename = entry.FullName.Replace('\\', '/');
                        if (filename.StartsWith('/') || filename.Split('/').Any(x => x is ".." or "." or "") ||
                            filename.Contains(':') || filename.Contains('\0')) throw new InvalidOperationException("Unsafe release entry.");
                        var mode = (entry.ExternalAttributes >> 16) & 0xF000;
                        if (mode == 0xA000) throw new InvalidOperationException("Symlink entry not allowed.");
                        if (entry.Length > MaxUnpackedBytes || total > MaxUnpackedBytes - entry.Length)
                            throw new InvalidOperationException("Update unpacked size exceeds 3 GB.");
                        total += entry.Length;
                        var item = Path.GetFullPath(Path.Combine(staged, filename.Replace('/', Path.DirectorySeparatorChar)));
                        if (!item.StartsWith(Path.GetFullPath(staged) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Unsafe release output path.");
                        Directory.CreateDirectory(Path.GetDirectoryName(item)!);
                        entry.ExtractToFile(item, overwrite: false);
                    }
                }
                if (!File.Exists(Path.Combine(staged, launch)))
                    throw new InvalidOperationException("The downloaded release is missing the requested app.");
                foreach (var file in Directory.EnumerateFiles(staged, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(staged, file);
                    var destination = Path.GetFullPath(Path.Combine(target, relative));
                    if (!destination.StartsWith(Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe destination path.");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination, overwrite: true);
                }
                Process.Start(new ProcessStartInfo(Path.Combine(target, launch)) { UseShellExecute = true, WorkingDirectory = target });
                return 0;
            }
            finally { try { Directory.Delete(staged, recursive: true); } catch { /* best effort */ } }
        }
        catch (ArgumentException exception) { Console.Error.WriteLine(exception.Message); return 2; }
        catch (Exception exception) { Console.Error.WriteLine("Zora update failed: " + exception.Message); return 1; }
    }
}
