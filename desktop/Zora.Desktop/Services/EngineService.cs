using System.IO;
using System.Diagnostics;
using System.Text.Json;
using Zora.Desktop.Models;
namespace Zora.Desktop.Services;
public sealed class EngineService
{
    private static (string command, List<string> args) Locate()
    {
        var baseDir = AppContext.BaseDirectory;
        var exe = Path.Combine(baseDir, "engine", "zora-engine.exe");
        if (File.Exists(exe)) return (exe, new List<string>());
        var parent = new DirectoryInfo(baseDir);
        for (var i = 0; i < 8 && parent is not null; i++, parent = parent.Parent)
        {
            var script = Path.Combine(parent.FullName, "engine", "zora_engine.py");
            if (File.Exists(script)) return ("python", new List<string> { script });
        }
        throw new FileNotFoundException("Zora engine not found. The release includes engine/zora-engine.exe, or install Python + UnityPy for development.");
    }
    private static async Task<T> RunAsync<T>(IEnumerable<string> parameters, CancellationToken cancellation = default)
    {
        var (command, prefix) = Locate();
        var info = new ProcessStartInfo(command) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var p in prefix.Concat(parameters)) info.ArgumentList.Add(p);
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException("Could not start the Zora engine");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellation);
        var errTask = process.StandardError.ReadToEndAsync(cancellation);
        await process.WaitForExitAsync(cancellation);
        var output = await outputTask;
        var stderr = await errTask;
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("The extractor did not respond. " + stderr[..Math.Min(stderr.Length, 400)]);
        using var document = JsonDocument.Parse(output);
        if (document.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString());
        if (process.ExitCode != 0) throw new InvalidOperationException(stderr.Length > 0 ? stderr : "Extractor exited with an error");
        return JsonSerializer.Deserialize<T>(output) ?? throw new InvalidOperationException("Invalid response from extractor");
    }
    public Task<ScanResult> ScanAsync(string path) => RunAsync<ScanResult>(["scan", "--source", path]);
    public Task<RecoverResult> RecoverAsync(string path, string destination) =>
        RunAsync<RecoverResult>(["recover", "--source", path, "--destination", destination]);
    public async Task<ExportResult> ExportAsync(string path, string destination, IEnumerable<string>? ids = null)
    {
        List<string> args = ["export", "--source", path, "--destination", destination];
        string? tempSelection = null;
        try
        {
            if (ids is not null)
            {
                var names = ids.ToArray();
                if (names.Length == 0) throw new ArgumentException("No assets selected");
                // The Windows CreateProcess command line is limited to ~32 KB.
                // An ID manifest supports thousands of assets without truncation.
                tempSelection = Path.Combine(Path.GetTempPath(), "ZoraSelection_" + Guid.NewGuid().ToString("N") + ".json");
                await File.WriteAllTextAsync(tempSelection, JsonSerializer.Serialize(names));
                args.Add("--ids-file"); args.Add(tempSelection);
            }
            return await RunAsync<ExportResult>(args);
        }
        finally
        {
            if (tempSelection is not null) try { File.Delete(tempSelection); } catch (IOException) { }
        }
    }
}
