using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

namespace Zora.Desktop.Services;

/// <summary>
/// Read-only inspection of managed .NET assemblies. Does not load or execute the
/// supplied assembly, patch licensing checks, or defeat application protections.
/// </summary>
public static class AssemblyInspector
{
    public sealed record Symbol(string Name, string Kind, string FullTypeName, string? MemberName = null, int Token = 0);
    public sealed record AssemblyInfo(string Name, string Path, long Size, List<Symbol> Types, Dictionary<string, List<Symbol>> Members);
    private const long MaxFileSize = 300 * 1024 * 1024;
    private const int MaxTypes = 12000;
    private const int MaxMembersPerType = 2000;

    public static bool Supports(string path) => File.Exists(path)
        && (Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase));

    public static AssemblyInfo Inspect(string path)
    {
        path = Path.GetFullPath(path);
        if (!Supports(path)) throw new InvalidOperationException("Choose a managed .NET DLL or EXE file.");
        var fileInfo = new FileInfo(path);
        if (fileInfo.Length > MaxFileSize) throw new InvalidOperationException("This assembly exceeds Zora's 300 MB inspection limit.");
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) throw new InvalidOperationException("This is a native DLL/EXE (not a managed .NET assembly). Zora can inspect its PE resources, but no original C# method bodies are present to decompile.");
        var md = pe.GetMetadataReader();
        var types = new List<Symbol>();
        var members = new Dictionary<string, List<Symbol>>(StringComparer.Ordinal);
        foreach (var handle in md.TypeDefinitions)
        {
            var def = md.GetTypeDefinition(handle);
            if (!def.GetDeclaringType().IsNil) continue;
            string name = md.GetString(def.Name);
            if (name == "<Module>") continue;
            string ns = md.GetString(def.Namespace);
            string qualified = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            types.Add(new Symbol(qualified, "Type", qualified, Token: MetadataTokens.GetToken(handle)));
            var elements = new List<Symbol>();
            foreach (var method in def.GetMethods())
            {
                if (elements.Count >= MaxMembersPerType) break;
                elements.Add(new Symbol(md.GetString(md.GetMethodDefinition(method).Name) + "()", "Method", qualified,
                    md.GetString(md.GetMethodDefinition(method).Name), MetadataTokens.GetToken(method)));
            }
            foreach (var prop in def.GetProperties())
            {
                if (elements.Count >= MaxMembersPerType) break;
                elements.Add(new Symbol(md.GetString(md.GetPropertyDefinition(prop).Name), "Property", qualified,
                    md.GetString(md.GetPropertyDefinition(prop).Name), MetadataTokens.GetToken(prop)));
            }
            foreach (var field in def.GetFields())
            {
                if (elements.Count >= MaxMembersPerType) break;
                elements.Add(new Symbol(md.GetString(md.GetFieldDefinition(field).Name), "Field", qualified,
                    md.GetString(md.GetFieldDefinition(field).Name), MetadataTokens.GetToken(field)));
            }
            foreach (var nested in def.GetNestedTypes())
            {
                if (elements.Count >= MaxMembersPerType) break;
                var nestedName = md.GetString(md.GetTypeDefinition(nested).Name);
                elements.Add(new Symbol(nestedName, "Nested Type", qualified));
            }
            members[qualified] = elements;
            if (types.Count >= MaxTypes) break;
        }
        return new AssemblyInfo(Path.GetFileName(path), path, fileInfo.Length, types, members);
    }

    public static string DecompileType(string assembly, string qualifiedName)
    {
        var decompiler = new CSharpDecompiler(assembly, new DecompilerSettings());
        return decompiler.DecompileTypeAsString(new FullTypeName(qualifiedName));
    }

    public static string DecompileModule(string assembly)
    {
        var decompiler = new CSharpDecompiler(assembly, new DecompilerSettings());
        return decompiler.DecompileWholeModuleAsString();
    }

    public static (int Exported, List<string> Errors) ExportCSharp(AssemblyInfo info, string destination)
    {
        var folder = Path.GetFullPath(destination);
        Directory.CreateDirectory(folder);
        int count = 0;
        var errors = new List<string>();
        var decompiler = new CSharpDecompiler(info.Path, new DecompilerSettings());
        foreach (var symbol in info.Types)
        {
            try
            {
                // Namespace paths are names only; never interpret separator characters.
                var parts = symbol.FullTypeName.Split('.');
                var namespaceParts = parts.SkipLast(1).Select(SafeFileName).ToArray();
                var targetFolder = Path.Combine(new[] { folder }.Concat(namespaceParts).ToArray());
                var target = Path.GetFullPath(Path.Combine(targetFolder, SafeFileName(parts[^1]) + ".cs"));
                if (!target.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe output path");
                Directory.CreateDirectory(targetFolder);
                var content = decompiler.DecompileTypeAsString(new FullTypeName(symbol.FullTypeName));
                // Repeated exports may not silently destroy edited files.
                if (File.Exists(target))
                {
                    for (int i = 1; i < 1000; i++)
                    {
                        var option = Path.Combine(targetFolder, SafeFileName(parts[^1]) + $"_{i}.cs");
                        if (!File.Exists(option)) { target = option; break; }
                    }
                    if (File.Exists(target)) throw new IOException("Too many source versions");
                }
                File.WriteAllText(target, content, new UTF8Encoding(false));
                count++;
            }
            catch (Exception ex)
            {
                errors.Add($"{symbol.FullTypeName}: {ex.Message}");
            }
        }
        var report = new StringBuilder($"Zora .NET decompilation report{Environment.NewLine}Assembly: {info.Name}{Environment.NewLine}Exported C# files: {count}{Environment.NewLine}");
        report.AppendLine("Decompiled source is approximate. Original comments/project files are not recoverable.");
        foreach (var error in errors.Take(150)) report.AppendLine("WARNING: " + error);
        File.WriteAllText(Path.Combine(folder, "ZORA_DECOMPILE_REPORT.txt"), report.ToString());
        return (count, errors);
    }

    private static string SafeFileName(string name)
    {
        string cleaned = new(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray());
        cleaned = cleaned.Trim(' ', '.');
        return string.IsNullOrEmpty(cleaned) ? "unnamed" : cleaned[..Math.Min(cleaned.Length, 110)];
    }
}
