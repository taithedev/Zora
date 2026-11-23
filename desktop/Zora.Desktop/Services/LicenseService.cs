using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Zora.Desktop.Services;

public sealed class LicenseService
{
    private sealed class LocalSettings
    {
        public string Username { get; set; } = "";
        public string InstallationId { get; set; } = Guid.NewGuid().ToString();
        public string ProtectedRefreshToken { get; set; } = "";
    }
    private sealed class TokenResponse
    {
        public string Username { get; set; } = "";
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
    }
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zora", "settings.json");
    private LocalSettings _settings;
    private string _access = "";
    // Public endpoint only. Neon connection credentials never enter desktop builds.
    // Release builds inject ZORA_PUBLIC_LICENSE_URL at build time; local
    // developer builds may use ZORA_API_URL in the environment.
    private const string ReleaseEndpoint = "https://br-frosty-bar-bbmdnhr7-zoralicense.compute.c-15.us-east-1.aws.neon.tech/";
    private string ApiUrl => (Environment.GetEnvironmentVariable("ZORA_API_URL") ??
        (ReleaseEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? ReleaseEndpoint : "")).TrimEnd('/');
    public string Username => _settings.Username;
    public bool Active => !string.IsNullOrEmpty(_access);
    public LicenseService()
    {
        try { _settings = JsonSerializer.Deserialize<LocalSettings>(File.ReadAllText(_settingsPath)) ?? new LocalSettings(); }
        catch { _settings = new LocalSettings(); }
        if (string.IsNullOrWhiteSpace(_settings.InstallationId)) _settings.InstallationId = Guid.NewGuid().ToString();
        Save();
    }
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings));
    }
    private static string Protect(string text) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser));
    private static string Unprotect(string text) => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(text), null, DataProtectionScope.CurrentUser));
    private Uri Endpoint(string path)
    {
        if (string.IsNullOrWhiteSpace(ApiUrl))
            throw new InvalidOperationException("Zora activation is not available. The owner must configure the Neon license function.");
        if (!Uri.TryCreate(ApiUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new InvalidOperationException("Zora license service must use HTTPS.");
        return new Uri(uri, path);
    }
    private static async Task<TokenResponse> ReadTokenAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            try { throw new InvalidOperationException(JsonDocument.Parse(text).RootElement.GetProperty("message").GetString()); }
            catch (KeyNotFoundException) { throw new InvalidOperationException("License activation was rejected"); }
        }
        return JsonSerializer.Deserialize<TokenResponse>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Invalid activation response");
    }
    private void Store(TokenResponse reply)
    {
        if (string.IsNullOrWhiteSpace(reply.AccessToken) || string.IsNullOrWhiteSpace(reply.RefreshToken)) throw new InvalidOperationException("Incomplete session response");
        _access = reply.AccessToken;
        _settings.Username = reply.Username;
        _settings.ProtectedRefreshToken = Protect(reply.RefreshToken);
        Save();
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private async Task<T> DiscordCallAsync<T>(HttpMethod method, string path) where T : class
    {
        if (!await VerifyAsync()) throw new InvalidOperationException("Your Zora license session has expired. Sign in again.");
        using var req = new HttpRequestMessage(method, Endpoint(path));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _access);
        using var res = await _http.SendAsync(req);
        var responseText = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            try
            {
                using var parsed = JsonDocument.Parse(responseText);
                var message = parsed.RootElement.GetProperty("message").GetString();
                throw new InvalidOperationException(message ?? "Discord request was not accepted.");
            }
            catch (JsonException) { throw new InvalidOperationException("Could not contact the Discord linking service."); }
            catch (KeyNotFoundException) { throw new InvalidOperationException("Discord request was not accepted."); }
        }
        return JsonSerializer.Deserialize<T>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("Invalid Discord sync response.");
    }
    public Task<DiscordLink> GetDiscordAsync() => DiscordCallAsync<DiscordLink>(HttpMethod.Get, "/v1/discord/me");
    public Task<DiscordStartResult> StartDiscordAsync() => DiscordCallAsync<DiscordStartResult>(HttpMethod.Post, "/v1/discord/start");
    public Task<DiscordLink> UnlinkDiscordAsync() => DiscordCallAsync<DiscordLink>(HttpMethod.Post, "/v1/discord/unlink");

    public async Task ActivateAsync(string username, string key)
    {
        using var response = await _http.PostAsJsonAsync(Endpoint("/v1/license/activate"), new { username, key, installationId = _settings.InstallationId });
        Store(await ReadTokenAsync(response));
    }
    public async Task<bool> RefreshAsync()
    {
        if (string.IsNullOrEmpty(_settings.ProtectedRefreshToken) || string.IsNullOrEmpty(_settings.Username)) return false;
        try
        {
            var refreshToken = Unprotect(_settings.ProtectedRefreshToken);
            using var reply = await _http.PostAsJsonAsync(Endpoint("/v1/license/refresh"), new { username = _settings.Username, installationId = _settings.InstallationId, refreshToken });
            if (!reply.IsSuccessStatusCode) { _access = ""; return false; }
            Store(await ReadTokenAsync(reply));
            return true;
        }
        catch { _access = ""; return false; }
    }
    public async Task<bool> VerifyAsync()
    {
        if (string.IsNullOrEmpty(_access)) return await RefreshAsync();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint("/v1/license/verify"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _access);
            using var reply = await _http.SendAsync(request);
            if (reply.IsSuccessStatusCode) return true;
            return await RefreshAsync();
        }
        catch { return false; }
    }
}
