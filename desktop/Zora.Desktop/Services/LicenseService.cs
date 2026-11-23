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
        public string ApiUrl { get; set; } = "http://127.0.0.1:8787";
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
    public string ApiUrl { get => _settings.ApiUrl; set { _settings.ApiUrl = value.TrimEnd('/'); Save(); } }
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
        if (!Uri.TryCreate(_settings.ApiUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new InvalidOperationException("Use an HTTPS license server URL (HTTP is only allowed for localhost).");
        return new Uri(uri, path);
    }
    private static async Task<TokenResponse> ReadTokenAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            try { throw new InvalidOperationException(JsonDocument.Parse(text).RootElement.GetProperty("message").GetString()); }
            catch (KeyNotFoundException) { throw new InvalidOperationException("License API rejected the request"); }
        }
        return JsonSerializer.Deserialize<TokenResponse>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Invalid license server response");
    }
    private void Store(TokenResponse reply)
    {
        if (string.IsNullOrWhiteSpace(reply.AccessToken) || string.IsNullOrWhiteSpace(reply.RefreshToken)) throw new InvalidOperationException("Incomplete session response");
        _access = reply.AccessToken;
        _settings.Username = reply.Username;
        _settings.ProtectedRefreshToken = Protect(reply.RefreshToken);
        Save();
    }
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
