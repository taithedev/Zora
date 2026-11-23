using System.Text.Json.Serialization;
namespace Zora.Desktop.Models;
public sealed class AssetRow
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("origin")] public string Origin { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("exportable")] public bool Exportable { get; set; }
    public string SizeText => Size < 1024 ? $"{Size} B" : Size < 1048576 ? $"{Size / 1024.0:F1} KB" : $"{Size / 1048576.0:F1} MB";
}
public sealed class ScanResult
{
    [JsonPropertyName("assets")] public List<AssetRow> Assets { get; set; } = [];
    [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = [];
}
public sealed class ExportResult
{
    [JsonPropertyName("exported")] public List<string> Exported { get; set; } = [];
    [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = [];
    [JsonPropertyName("count")] public int Count { get; set; }
}

public sealed class RecoverResult
{
    [JsonPropertyName("project_path")] public string ProjectPath { get; set; } = "";
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = [];
}
