using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Zora.Admin;

internal static class Program
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static string _api = "";
    private static string _token = "";

    private static async Task<int> Main()
    {
        Console.Title = "Zora Owner Console";
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Z O R A  /  OWNER CONSOLE");
        Console.ResetColor();
        Console.WriteLine("Private license administration · $25 per license\n");
        var defaultUrl = Environment.GetEnvironmentVariable("ZORA_API_URL") ?? "http://127.0.0.1:8787";
        Console.Write($"API URL [{defaultUrl}]: ");
        _api = (Console.ReadLine() ?? "").Trim();
        if (_api.Length == 0) _api = defaultUrl;
        _api = _api.TrimEnd('/');
        if (!Uri.TryCreate(_api, UriKind.Absolute, out var url) ||
            (url.Scheme != "https" && !(url.Scheme == "http" && url.IsLoopback)))
        {
            Error("Use HTTPS, except for localhost development.");
            return 1;
        }
        _token = Environment.GetEnvironmentVariable("ZORA_ADMIN_TOKEN") ?? "";
        if (string.IsNullOrWhiteSpace(_token))
        {
            Console.Write("Owner API token (hidden): ");
            _token = ReadSecret();
        }
        if (_token.Length < 24) { Error("Invalid owner API token."); return 1; }
        while (true)
        {
            Console.WriteLine("\n[1] Stats    [2] List accounts    [3] Generate keys");
            Console.WriteLine("[4] Ban      [5] Unban            [6] Revoke");
            Console.WriteLine("[7] Reset devices              [8] Audit log    [0] Quit");
            Console.Write("Zora > ");
            var choice = (Console.ReadLine() ?? "").Trim();
            try
            {
                switch (choice)
                {
                    case "1": await Stats(); break;
                    case "2": await Accounts(); break;
                    case "3": await Generate(); break;
                    case "4": await ChangeStatus("banned"); break;
                    case "5": await ChangeStatus("active"); break;
                    case "6": await ChangeStatus("revoked"); break;
                    case "7": await ResetDevices(); break;
                    case "8": await Audit(); break;
                    case "0": return 0;
                    default: Console.WriteLine("Choose an option from 0 to 8."); break;
                }
            }
            catch (Exception ex) { Error(ex.Message); }
        }
    }

    private static string ReadSecret()
    {
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
            if (key.Key == ConsoleKey.Backspace) { if (chars.Count > 0) chars.RemoveAt(chars.Count - 1); continue; }
            if (!char.IsControl(key.KeyChar) && chars.Count < 256) chars.Add(key.KeyChar);
        }
        return new string(chars.ToArray());
    }

    private static async Task<JsonElement> Api(string path, HttpMethod method, object? payload = null)
    {
        using var req = new HttpRequestMessage(method, _api + path);
        req.Headers.Add("x-zora-admin-token", _token);
        if (payload != null) req.Content = JsonContent.Create(payload);
        using var response = await Http.SendAsync(req);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        if (!response.IsSuccessStatusCode)
        {
            var message = document.RootElement.TryGetProperty("message", out var m) ? m.GetString() : response.ReasonPhrase;
            throw new InvalidOperationException($"Server rejected request: {message}");
        }
        return document.RootElement.Clone();
    }

    private static string Field(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return "-";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "-" : value.ToString();
    }
    private static void Error(string text)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Error: " + text);
        Console.ResetColor();
    }
    private static async Task Stats()
    {
        var data = await Api("/v1/admin/stats", HttpMethod.Get);
        Console.WriteLine($"Total: {Field(data, "total")} | Activated: {Field(data, "activated")} | Banned: {Field(data, "banned")} | Revoked: {Field(data, "revoked")}");
    }
    private static async Task Accounts()
    {
        var data = await Api("/v1/admin/licenses?limit=200", HttpMethod.Get);
        Console.WriteLine($"{"LICENSE ID",-38} {"KEY (MASKED)",-21} {"USERNAME",-24} {"STATUS",-9} {"DEV"}");
        foreach (var license in data.GetProperty("licenses").EnumerateArray())
        {
            Console.WriteLine($"{Field(license, "id"),-38} {Field(license, "key_label"),-21} {Field(license, "username"),-24} {Field(license, "status"),-9} {Field(license, "devices")}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"   Created: {Field(license, "created_at")}  Last seen: {Field(license, "last_seen_at")}");
            Console.ResetColor();
        }
    }
    private static int ReadNumber(string prompt, int minimum, int maximum, int fallback)
    {
        Console.Write($"{prompt} [{fallback}]: ");
        var s = (Console.ReadLine() ?? "").Trim();
        if (s.Length == 0) return fallback;
        if (!int.TryParse(s, out var number) || number < minimum || number > maximum)
            throw new ArgumentException($"Enter a number between {minimum} and {maximum}.");
        return number;
    }
    private static async Task Generate()
    {
        var quantity = ReadNumber("Quantity (1-500)", 1, 500, 1);
        var devices = ReadNumber("Max activations per license (1-5)", 1, 5, 1);
        var data = await Api("/v1/admin/licenses/bulk", HttpMethod.Post, new { quantity, maxDevices = devices });
        var array = data.GetProperty("licenses").EnumerateArray().ToArray();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Generated {array.Length} cryptographically random Zora license keys.");
        Console.ResetColor();
        Console.Write("Save full keys to a local JSON file? [Y/n]: ");
        var save = (Console.ReadLine() ?? "y").Trim();
        if (!save.Equals("n", StringComparison.OrdinalIgnoreCase))
        {
            Console.Write("New private filename [Zora-Keys.json]: ");
            var filename = (Console.ReadLine() ?? "").Trim();
            if (filename.Length == 0) filename = "Zora-Keys.json";
            var output = JsonSerializer.Serialize(new { priceUsd = 25, generatedAt = DateTimeOffset.UtcNow, licenses = array }, new JsonSerializerOptions { WriteIndented = true });
            await using var file = new FileStream(filename, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using var writer = new StreamWriter(file);
            await writer.WriteAsync(output);
            Console.WriteLine($"Keys saved to {Path.GetFullPath(filename)}. Store securely and do not commit this file.");
        }
        else
        {
            foreach (var license in array) Console.WriteLine(Field(license, "key"));
            Console.WriteLine("These complete keys will NOT be retrievable from the API afterward.");
        }
    }
    private static async Task ChangeStatus(string status)
    {
        var id = LicenseId();
        Console.Write($"Change {id} to '{status}'? Type YES: ");
        if (Console.ReadLine() != "YES") return;
        var result = await Api($"/v1/admin/licenses/{id}/status", HttpMethod.Post, new { status });
        Console.WriteLine($"License {Field(result, "id")} is now {Field(result, "status")}");
    }
    private static async Task ResetDevices()
    {
        var id = LicenseId();
        Console.Write($"Reset all device activations for {id}? Type YES: ");
        if (Console.ReadLine() != "YES") return;
        var result = await Api($"/v1/admin/licenses/{id}/reset-devices", HttpMethod.Post);
        Console.WriteLine($"Removed {Field(result, "removed")} activations. This invalidates prior sessions.");
    }
    private static string LicenseId()
    {
        Console.Write("License UUID (from List accounts): ");
        var value = (Console.ReadLine() ?? "").Trim();
        if (!Guid.TryParse(value, out var id)) throw new ArgumentException("Enter a valid UUID from the accounts list.");
        return id.ToString("D");
    }
    private static async Task Audit()
    {
        var value = await Api("/v1/admin/audit", HttpMethod.Get);
        foreach (var evt in value.GetProperty("events").EnumerateArray())
            Console.WriteLine($"{Field(evt, "occurred_at"),-30} {Field(evt, "action"),-25} {Field(evt, "actor"),-15} {Field(evt, "license_id")}");
    }
}
