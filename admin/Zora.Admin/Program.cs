using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Zora.Admin;

internal static class Program
{
    // The owner EXE needs only a password. DB credentials never leave the Neon Function.
    private const string DefaultEndpoint = "https://br-frosty-bar-bbmdnhr7-zoralicense.compute.c-15.us-east-1.aws.neon.tech";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(35) };
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private static string _baseUrl = DefaultEndpoint;
    private static string _accessToken = "";
    private static DateTimeOffset _expires;

    private static async Task<int> Main()
    {
        Console.Title = "Zora Owner Console";
        Console.BackgroundColor = ConsoleColor.Black;
        Console.ForegroundColor = ConsoleColor.White;
        Console.Clear();
        var overrideUrl = Environment.GetEnvironmentVariable("ZORA_OWNER_API_URL");
        if (!string.IsNullOrWhiteSpace(overrideUrl)) _baseUrl = overrideUrl.TrimEnd('/');
        if (!Uri.TryCreate(_baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            WriteError("Owner endpoint must use HTTPS, except for localhost testing.");
            return 1;
        }
        Title();
        Console.WriteLine("  Enter your owner password to unlock Neon administration.\n");
        if (!await SignIn()) return 1;
        await Dashboard();
        Help();
        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("\n  ZORA  /  ");
            Console.ForegroundColor = ConsoleColor.White;
            var selection = (Console.ReadLine() ?? "").Trim();
            if (selection.Equals("quit", StringComparison.OrdinalIgnoreCase) || selection == "0") break;
            if (selection.Equals("help", StringComparison.OrdinalIgnoreCase) || selection == "?") { Help(); continue; }
            if (!int.TryParse(selection, out var code)) { WriteError("Use a command number or type help."); continue; }
            try
            {
                if (_expires <= DateTimeOffset.UtcNow && code != 47 && code != 46) throw new InvalidOperationException("Session expired. Use command 48 to sign in again.");
                switch (code)
                {
                    case 1: await Dashboard(); break;
                    case 2: await ListLicenses(); break;
                    case 3: await SearchLicenses(); break;
                    case 4: await LicenseDetails(); break;
                    case 5: await GenerateKeys(false); break;
                    case 6: await GenerateKeys(true); break;
                    case 7: await ListLicenses("status=active"); break;
                    case 8: await ListLicenses("status=banned"); break;
                    case 9: await ListLicenses("status=revoked"); break;
                    case 10: await ListLicenses("filter=unactivated"); break;
                    case 11: await ListLicenses("filter=activated"); break;
                    case 12: await ListLicenses("filter=inactive"); break;
                    case 13: await ListLicenses("filter=expiring"); break;
                    case 14: await ListLicenses("filter=expired"); break;
                    case 15: await Audit("hours=24&action=license.created"); break;
                    case 16: await Audit("hours=24&action=license.activated"); break;
                    case 17: await SetStatus("banned"); break;
                    case 18: await SetStatus("active"); break;
                    case 19: await SetStatus("revoked"); break;
                    case 20: await ResetDevices(); break;
                    case 21: await UpdateLicense("maxDevices", "Device limit (1-5)"); break;
                    case 22: await UpdateLicense("note", "New note"); break;
                    case 23: await ClearNote(); break;
                    case 24: await UpdateExpiration(); break;
                    case 25: await ClearExpiration(); break;
                    case 26: await ExtendExpiration(); break;
                    case 27: await UpdateLicense("username", "New username"); break;
                    case 28: await ClearUsername(); break;
                    case 29: await Devices(); break;
                    case 30: await RemoveDevice(); break;
                    case 31: await BulkStatus(); break;
                    case 32: await Audit(); break;
                    case 33: await Audit("action=" + Escape(Prompt("Action contains"))); break;
                    case 34: await Audit("licenseId=" + Escape(LicenseId())); break;
                    case 35: await Audit("hours=24"); break;
                    case 36: await Activity(); break;
                    case 37: await Export("licenses", "csv"); break;
                    case 38: await Export("licenses", "json"); break;
                    case 39: await Export("audit", "csv"); break;
                    case 40: await Export("audit", "json"); break;
                    case 41: await Security(); break;
                    case 42: await Sessions(); break;
                    case 43: await RevokeSessions(false); break;
                    case 44: await RevokeSessions(true); break;
                    case 45: await ChangePassword(); break;
                    case 46: await Health(); break;
                    case 47: await Logout(); break;
                    case 48: await SignIn(); break;
                    case 49: Help(); break;
                    default: WriteError("Unknown command. Type help."); break;
                }
            }
            catch (Exception ex) { WriteError(ex.Message); }
        }
        if (!string.IsNullOrWhiteSpace(_accessToken))
        {
            try { await Api("/v1/admin/logout", HttpMethod.Post); } catch { /* exit anyway */ }
        }
        _accessToken = "";
        Console.WriteLine("  Signed out.");
        return 0;
    }

    private static void Title()
    {
        Console.WriteLine();
        Console.WriteLine("  Z O R A");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  O W N E R   C O N S O L E   /   NEON");
        Console.WriteLine("  Monochrome  ·  $25 per license  ·  Private administration");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine();
    }
    private static void Help()
    {
        Console.WriteLine("\n  OVERVIEW & KEYS                        ACCOUNT MANAGEMENT");
        Console.WriteLine("  01  Overview      02  Browse           17  Ban             18  Unban");
        Console.WriteLine("  03  Search        04  License detail   19  Revoke          20  Reset devices");
        Console.WriteLine("  05  Generate key  06  Generate bulk    21  Device limit    22  Edit note");
        Console.WriteLine("  07  Active        08  Banned           23  Clear note      24  Set expiry");
        Console.WriteLine("  09  Revoked       10  Unactivated      25  Clear expiry    26  Extend expiry");
        Console.WriteLine("  11  Activated     12  Inactive         27  Rename user     28  Clear username");
        Console.WriteLine("  13  Expiring      14  Expired          29  View devices    30  Remove device");
        Console.WriteLine("  15  New 24h       16  Activated 24h    31  Bulk status");
        Console.WriteLine("\n  AUDIT, REPORTS & EXPORTS               OWNER SECURITY");
        Console.WriteLine("  32  Audit log     33  Filter action    41  Security        42  Sessions");
        Console.WriteLine("  34  License audit 35  Last 24h         43  Revoke others   44  Revoke all");
        Console.WriteLine("  36  Activity 14d  37  Licenses CSV     45  New password    46  Neon health");
        Console.WriteLine("  38  Licenses JSON 39  Audit CSV        47  Logout          48  Login");
        Console.WriteLine("  40  Audit JSON    49  Help             00  Quit");
    }
    private static async Task<bool> SignIn()
    {
        _accessToken = "";
        Console.Write("  Owner password: ");
        var password = ReadSecret();
        if (password.Length == 0) return false;
        try
        {
            var result = await Api("/v1/admin/login", HttpMethod.Post, new { password }, authenticated: false);
            _accessToken = Field(result, "accessToken");
            _expires = DateTimeOffset.Parse(Field(result, "expiresAt"));
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  Owner verified. Neon session active.");
            return true;
        }
        catch (Exception ex) { WriteError(ex.Message); return false; }
        finally { password = ""; }
    }
    private static string ReadSecret()
    {
        var chars = new List<char>();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
            if (k.Key == ConsoleKey.Backspace) { if (chars.Count > 0) chars.RemoveAt(chars.Count - 1); continue; }
            if (!char.IsControl(k.KeyChar) && chars.Count < 256) chars.Add(k.KeyChar);
        }
        var output = new string(chars.ToArray());
        chars.Clear();
        return output;
    }
    private static async Task<JsonElement> Api(string path, HttpMethod method, object? value = null, bool authenticated = true)
    {
        using var request = new HttpRequestMessage(method, _baseUrl + path);
        request.Headers.UserAgent.ParseAdd("ZoraAdmin/0.3");
        if (authenticated)
        {
            if (string.IsNullOrEmpty(_accessToken)) throw new InvalidOperationException("Not signed in. Use 48 to enter your owner password.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }
        if (value != null) request.Content = JsonContent.Create(value);
        using var response = await Http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        JsonElement result;
        try { using var doc = JsonDocument.Parse(content); result = doc.RootElement.Clone(); }
        catch { throw new InvalidOperationException($"Neon returned HTTP {(int)response.StatusCode}, not JSON."); }
        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode == 401 && authenticated) _accessToken = "";
            throw new InvalidOperationException($"Neon: {Field(result, "message")}");
        }
        return result;
    }
    private static string Field(JsonElement e, string key)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return "-";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "-" : value.ToString();
    }
    private static string Prompt(string text, string fallback = "")
    {
        Console.Write($"  {text}{(fallback.Length > 0 ? $" [{fallback}]" : "")}: ");
        var entry = (Console.ReadLine() ?? "").Trim();
        return entry.Length == 0 ? fallback : entry;
    }
    private static string LicenseId()
    {
        var id = Prompt("License UUID");
        if (!Guid.TryParse(id, out var parsed)) throw new ArgumentException("Enter a license UUID from the browser.");
        return parsed.ToString("D");
    }
    private static int Number(string label, int minimum, int maximum, int fallback)
    {
        var value = Prompt(label, fallback.ToString());
        if (!int.TryParse(value, out var n) || n < minimum || n > maximum) throw new ArgumentException($"Use {minimum}-{maximum}.");
        return n;
    }
    private static bool Confirm(string action)
    {
        Console.Write($"  {action}. Type YES to continue: ");
        return (Console.ReadLine() ?? "") == "YES";
    }
    private static string Escape(string s) => Uri.EscapeDataString(s);
    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("  ERROR / " + message);
        Console.ForegroundColor = ConsoleColor.White;
    }
    private static void Show(JsonElement element, params string[] keys)
    {
        foreach (var key in keys) Console.WriteLine($"  {key,-22} {Field(element, key)}");
    }
    private static async Task Dashboard()
    {
        var info = await Api("/v1/admin/stats", HttpMethod.Get);
        Console.WriteLine("\n  ZORA  /  OVERVIEW");
        Show(info, "total", "active", "activated", "unactivated", "banned", "revoked", "devices", "expired", "expiring_30_days", "created_24h", "activated_24h");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  License price: $25. Generated keys are NOT proof of payment.");
        Console.ForegroundColor = ConsoleColor.White;
    }
    private static async Task ListLicenses(string query = "")
    {
        var offset = 0;
        do
        {
            var uri = "/v1/admin/licenses?limit=50&offset=" + offset + (query.Length > 0 ? "&" + query : "");
            var result = await Api(uri, HttpMethod.Get);
            var entries = result.GetProperty("licenses").EnumerateArray().ToArray();
            Console.WriteLine($"\n  LICENSES  /  {offset + 1}-{offset + entries.Length} OF {Field(result,"total")}");
            foreach (var row in entries)
            {
                Console.WriteLine($"  {Field(row,"key_label"),-20}  {Field(row,"username"),-24}  {Field(row,"status"),-9}  {Field(row,"devices")} dev");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  ID {Field(row,"id")}  Created {Field(row,"created_at")}");
                Console.ForegroundColor = ConsoleColor.White;
            }
            offset += entries.Length;
            if (entries.Length == 0 || offset >= int.Parse(Field(result,"total"))) break;
        } while (Prompt("Next page? (y/N)").Equals("y", StringComparison.OrdinalIgnoreCase));
    }
    private static async Task SearchLicenses()
    {
        var term = Prompt("Search username, masked key, note, or UUID");
        if (term.Length != 0) await ListLicenses("q=" + Escape(term));
    }
    private static async Task LicenseDetails()
    {
        var result = await Api("/v1/admin/licenses/" + LicenseId(), HttpMethod.Get);
        var license = result.GetProperty("license");
        Show(license,"id","key_label","username","status","max_devices","devices","created_at","activated_at","last_seen_at","expires_at","note");
    }
    private static async Task GenerateKeys(bool bulk)
    {
        var quantity = bulk ? Number("How many keys",1,500,25) : 1;
        var maxDevices = Number("Devices per license",1,5,1);
        var note = Prompt("Optional internal note");
        if (!Confirm($"Generate {quantity} license(s)")) return;
        var result = await Api("/v1/admin/licenses/bulk", HttpMethod.Post, new { quantity, maxDevices, note });
        var keys = result.GetProperty("licenses").EnumerateArray().Select(x => x.Clone()).ToArray();
        Console.WriteLine($"  Generated {keys.Length} cryptographic license keys.");
        var name = Prompt("Save to new private JSON file", $"Zora-Keys-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        if (name.Length == 0) throw new InvalidOperationException("A filename is required to avoid losing generated keys.");
        var data = JsonSerializer.Serialize(new { createdAt = DateTimeOffset.UtcNow, priceUsd = 25, licenses = keys }, Pretty);
        using (var stream = new FileStream(CreateNewFile(name), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, Encoding.UTF8)) await writer.WriteAsync(data);
        Console.WriteLine("  Saved once. The server cannot recover plaintext keys later.");
    }
    // Reserve a unique filename without overwriting any existing owner's keys.
    private static string CreateNewFile(string name)
    {
        var full = Path.GetFullPath(name);
        if (File.Exists(full)) throw new IOException("Destination exists. Choose a new filename.");
        return full;
    }
    private static async Task SetStatus(string status)
    {
        var id = LicenseId();
        if (!Confirm($"Set {id} to {status}")) return;
        var result = await Api($"/v1/admin/licenses/{id}/status", HttpMethod.Post, new { status });
        Show(result,"id","status");
    }
    private static async Task ResetDevices()
    {
        var id = LicenseId();
        if (!Confirm("Invalidate all device sessions for " + id)) return;
        var result = await Api($"/v1/admin/licenses/{id}/reset-devices", HttpMethod.Post);
        Show(result,"removed","reset");
    }
    private static async Task UpdateLicense(string key, string label)
    {
        var id = LicenseId(); var val = Prompt(label);
        if (string.IsNullOrEmpty(val)) return;
        object input = key switch
        {
            "maxDevices" => new { maxDevices = int.TryParse(val, out var n) ? n : -1 },
            "note" => new { note = val },
            "username" => new { username = val },
            _ => throw new ArgumentException("Unknown field")
        };
        if (!Confirm("Update " + label)) return;
        var result = await Api($"/v1/admin/licenses/{id}/update", HttpMethod.Patch, input);
        Show(result,"id","username","note","max_devices");
    }
    private static async Task ClearNote()
    {
        var id = LicenseId();
        if (!Confirm("Clear internal note")) return;
        var result = await Api($"/v1/admin/licenses/{id}/update", HttpMethod.Patch, new { note = "" });
        Show(result,"id","note");
    }
    private static async Task UpdateExpiration()
    {
        var id = LicenseId();
        var value = Prompt("Expiration (YYYY-MM-DD, UTC)");
        if (!DateTime.TryParse(value, out var date)) throw new ArgumentException("Enter a valid date.");
        if (!Confirm("Set license expiration")) return;
        var result = await Api($"/v1/admin/licenses/{id}/update", HttpMethod.Patch,
            new { expiresAt = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc).ToString("O") });
        Show(result,"id","expires_at");
    }
    private static async Task ClearExpiration()
    {
        var id = LicenseId();
        if (!Confirm("Remove expiration date")) return;
        var result = await Api($"/v1/admin/licenses/{id}/update", HttpMethod.Patch, new { expiresAt = (string?)null });
        Show(result,"id","expires_at");
    }
    private static async Task ExtendExpiration()
    {
        var id = LicenseId(); var days = Number("Additional days",1,3650,30);
        if (!Confirm($"Extend license by {days} days")) return;
        var result = await Api($"/v1/admin/licenses/{id}/extend", HttpMethod.Post,new { days });
        Show(result,"id","expires_at");
    }
    private static async Task ClearUsername()
    {
        var id = LicenseId();
        if (!Confirm("Unbind username and invalidate ALL activated devices")) return;
        var result = await Api($"/v1/admin/licenses/{id}/clear-username", HttpMethod.Post);
        Show(result,"username","removedDevices");
    }
    private static async Task Devices()
    {
        var id = LicenseId();
        var result = await Api($"/v1/admin/licenses/{id}/devices", HttpMethod.Get);
        Console.WriteLine("  ACTIVATION DEVICES");
        foreach (var d in result.GetProperty("devices").EnumerateArray()) Show(d,"id","created_at","last_seen_at");
    }
    private static async Task RemoveDevice()
    {
        var id = LicenseId();
        var deviceId = Prompt("Device UUID (use command 29)");
        if (!Guid.TryParse(deviceId,out var parsed)) throw new ArgumentException("Invalid device ID.");
        if (!Confirm("Remove device activation")) return;
        var result = await Api($"/v1/admin/licenses/{id}/devices/{parsed:D}", HttpMethod.Delete);
        Show(result,"removed");
    }
    private static async Task BulkStatus()
    {
        var raw = Prompt("License UUIDs, separated with commas");
        var ids = raw.Split(',',StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length < 1 || ids.Length > 100 || ids.Any(x=>!Guid.TryParse(x,out _))) throw new ArgumentException("Provide 1-100 valid UUIDs.");
        var status = Prompt("Status (active/banned/revoked)").ToLowerInvariant();
        if (!new[] {"active","banned","revoked"}.Contains(status)) throw new ArgumentException("Invalid status.");
        if (!Confirm($"Change {ids.Length} licenses to {status}")) return;
        var result = await Api("/v1/admin/licenses/bulk-status", HttpMethod.Post, new { ids, status });
        Show(result,"requested","updated","status");
    }
    private static async Task Audit(string query = "")
    {
        var result = await Api("/v1/admin/audit?limit=100" + (query.Length > 0 ? "&"+query : ""), HttpMethod.Get);
        Console.WriteLine($"  AUDIT / SHOWING {result.GetProperty("events").GetArrayLength()} OF {Field(result,"total")}");
        foreach (var row in result.GetProperty("events").EnumerateArray())
            Console.WriteLine($"  {Field(row,"occurred_at"),-29}  {Field(row,"action"),-27} {Field(row,"license_id")}");
    }
    private static async Task Activity()
    {
        var result = await Api("/v1/admin/activity", HttpMethod.Get);
        Console.WriteLine("  ACTIVITY / LAST 14 DAYS");
        foreach (var row in result.GetProperty("activity").EnumerateArray())
            Console.WriteLine($"  {Field(row,"day"),-14} {Field(row,"action"),-30} {Field(row,"count")}");
    }
    private static async Task Export(string category, string format)
    {
        var arrayName = category == "licenses" ? "licenses" : "events";
        var path = category == "licenses" ? "/v1/admin/licenses" : "/v1/admin/audit";
        var all = new List<JsonElement>();
        for (var offset = 0; ; offset += 500)
        {
            var data = await Api($"{path}?limit=500&offset={offset}", HttpMethod.Get);
            var rows = data.GetProperty(arrayName).EnumerateArray().Select(x=>x.Clone()).ToArray();
            all.AddRange(rows);
            if (rows.Length < 500) break;
        }
        var filename = Prompt("New export filename", $"Zora-{category}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.{format}");
        var destination = CreateNewFile(filename);
        string content;
        if (format == "json") content = JsonSerializer.Serialize(all, Pretty);
        else
        {
            string[] columns = category == "licenses"
                ? ["id","key_label","username","status","max_devices","devices","created_at","activated_at","last_seen_at","expires_at","note"]
                : ["id","occurred_at","action","actor","license_id","details"];
            content = string.Join(',', columns) + "\r\n" + string.Join("\r\n", all.Select(row => string.Join(',', columns.Select(key => CsvValue(Field(row,key))))));
        }
        using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, Encoding.UTF8)) await writer.WriteAsync(content);
        Console.WriteLine($"  Exported {all.Count} records to {destination}.");
        Console.WriteLine("  Exported keys are MASKED; plaintext keys cannot be recovered.");
    }
    private static string CsvValue(string input)
    {
        if (input.Length > 0 && "=+-@".Contains(input[0])) input = "'" + input;
        return "\"" + input.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
    }
    private static async Task Security()
    {
        var result = await Api("/v1/admin/security", HttpMethod.Get);
        Show(result,"updated_at","password_version","active_sessions","sessionExpiresAt");
    }
    private static async Task Sessions()
    {
        var result = await Api("/v1/admin/sessions", HttpMethod.Get);
        Console.WriteLine("  OWNER SESSIONS");
        foreach(var row in result.GetProperty("sessions").EnumerateArray())
            Console.WriteLine($"  {Field(row,"id")}  {Field(row,"last_seen_at")}  {Field(row,"user_agent")}");
    }
    private static async Task RevokeSessions(bool all)
    {
        if (!Confirm(all ? "Log out ALL owner consoles including this one" : "Log out all OTHER owner consoles")) return;
        var path = all ? "/v1/admin/sessions/revoke-all" : "/v1/admin/sessions/revoke-others";
        var result = await Api(path, HttpMethod.Post);
        Show(result,"revoked");
        if (all) _accessToken = "";
    }
    private static async Task ChangePassword()
    {
        Console.Write("  Current owner password: ");var current=ReadSecret();
        Console.Write("  New owner password (16+ characters): ");var next=ReadSecret();
        Console.Write("  Confirm new password: ");var confirmation=ReadSecret();
        if (next!=confirmation) throw new ArgumentException("Passwords did not match.");
        if (next.Length<16) throw new ArgumentException("New password must contain at least 16 characters.");
        if (!Confirm("Rotate owner password and revoke all sessions")) return;
        await Api("/v1/admin/password",HttpMethod.Post,new { currentPassword=current,newPassword=next });
        current="";next="";confirmation="";_accessToken="";
        Console.WriteLine("  Password updated in Neon. Use command 48 to sign in with the new password.");
    }
    private static async Task Health()
    {
        var result = await Api("/health",HttpMethod.Get,authenticated:false);
        Show(result,"ok","name","priceUsd");
    }
    private static async Task Logout()
    {
        if (!string.IsNullOrEmpty(_accessToken)) await Api("/v1/admin/logout",HttpMethod.Post);
        _accessToken="";
        Console.WriteLine("  Signed out of the owner session.");
    }
}
