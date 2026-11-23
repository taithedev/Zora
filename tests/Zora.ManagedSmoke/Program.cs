using Zora.Desktop.Services;

// Smoke test on Windows CI: browse an actual managed assembly and decompile a type.
var path = typeof(AssemblyInspector).Assembly.Location;
if (!File.Exists(path)) throw new InvalidOperationException("Test assembly not found");
var assembly = AssemblyInspector.Inspect(path);
var target = assembly.Types.FirstOrDefault(t => t.Name.EndsWith("AssemblyInspector", StringComparison.Ordinal));
if (target is null) throw new InvalidOperationException("Managed assembly metadata was not listed");
var code = AssemblyInspector.DecompileType(path, target.FullTypeName);
if (!code.Contains("class AssemblyInspector", StringComparison.Ordinal) &&
    !code.Contains("static class AssemblyInspector", StringComparison.Ordinal))
    throw new InvalidOperationException("AssemblyInspector source not recovered");
var output = Path.Combine(Path.GetTempPath(), "Zora_ManagedSmoke_" + Guid.NewGuid().ToString("N"));
try
{
    var result = AssemblyInspector.ExportCSharp(assembly, output);
    if (result.Exported < 1 || !File.Exists(Path.Combine(output,"ZORA_DECOMPILE_REPORT.txt")))
        throw new InvalidOperationException("C# export failed");
    Console.WriteLine($"Managed DLL inspection OK: {assembly.Types.Count} types, {result.Exported} .cs files");
}
finally
{
    try { Directory.Delete(output, recursive: true); } catch (IOException) { }
}
