using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Zora.Desktop.Views;

public partial class ZuroPanel : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly ObservableCollection<LicenseRow> _licenses = [];
    private string? _ownerToken;
    private bool _busy;
    private const string ReleaseEndpoint = "https://br-frosty-bar-bbmdnhr7-zoralicense.compute.c-15.us-east-1.aws.neon.tech/";
    private string ApiBase => (Environment.GetEnvironmentVariable("ZORA_API_URL") ?? ReleaseEndpoint).TrimEnd('/') + "/";
    private sealed class LicenseRow
    {
        public Guid Id { get; set; }
        public string KeyLabel { get; set; } = "";
        public string? Username { get; set; }
        public string Status { get; set; } = "";
        public int Devices { get; set; }
    }
    private sealed class LicenseList
    {
        public List<LicenseRow> Licenses { get; set; } = [];
    }
    private sealed class KeyList
    {
        public List<GeneratedKey> Licenses { get; set; } = [];
    }
    private sealed class GeneratedKey
    {
        public string Key { get; set; } = "";
    }
    public ZuroPanel()
    {
        InitializeComponent();
        LicensesGrid.ItemsSource = _licenses;
    }
    private Uri Endpoint(string route)
    {
        var baseUri = new Uri(ApiBase);
        if (baseUri.Scheme != Uri.UriSchemeHttps && !(baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback))
            throw new InvalidOperationException("Owner access requires HTTPS.");
        return new Uri(baseUri, route.TrimStart('/'));
    }
    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? payload = null, bool includeToken = true)
    {
        using var request = new HttpRequestMessage(method, Endpoint(path));
        if (payload is not null) request.Content = JsonContent.Create(payload);
        if (includeToken && !string.IsNullOrWhiteSpace(_ownerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _ownerToken);
        using var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                using var parsed = JsonDocument.Parse(body);
                throw new InvalidOperationException(parsed.RootElement.TryGetProperty("message", out var message)
                    ? message.GetString() ?? "Owner request failed" : "Owner request failed");
            }
            catch (JsonException) { throw new InvalidOperationException($"Neon returned HTTP {(int)response.StatusCode}."); }
        }
        return JsonDocument.Parse(body);
    }
    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var password = OwnerPassword.Password;
        if (password.Length == 0) { OwnerStatus.Text = "Enter your owner password."; return; }
        try
        {
            _busy = true; LoginButton.IsEnabled = false;
            OwnerStatus.Text = "Checking Neon owner credentials...";
            using var response = await SendAsync(HttpMethod.Post, "v1/admin/login", new { password }, includeToken:false);
            _ownerToken = response.RootElement.GetProperty("accessToken").GetString();
            OwnerPassword.Clear();
            OwnerLoginPanel.Visibility = Visibility.Collapsed;
            OwnerDashboard.Visibility = Visibility.Visible;
            await RefreshAsync();
        }
        catch (Exception ex) { OwnerStatus.Text = ex.Message; }
        finally { _busy = false; LoginButton.IsEnabled = true; }
    }
    private void Password_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Login_Click(sender, e); e.Handled = true; }
    }
    private async Task RefreshAsync()
    {
        try
        {
            using var stats = await SendAsync(HttpMethod.Get, "v1/admin/stats");
            var data = stats.RootElement;
            TotalCount.Text = data.GetProperty("total").GetInt32().ToString("N0");
            ActivatedCount.Text = data.GetProperty("activated").GetInt32().ToString("N0");
            BannedCount.Text = data.GetProperty("banned").GetInt32().ToString("N0");
            DevicesCount.Text = data.GetProperty("devices").GetInt32().ToString("N0");
            using var listing = await SendAsync(HttpMethod.Get, "v1/admin/licenses?limit=100");
            var records = JsonSerializer.Deserialize<LicenseList>(listing.RootElement.GetRawText(), JsonOptions)?.Licenses ?? [];
            _licenses.Clear(); foreach (var item in records) _licenses.Add(item);
            OwnerStatus.Text = $"Synced with Neon · Showing {_licenses.Count} recent accounts";
        }
        catch (Exception ex)
        {
            OwnerStatus.Text = ex.Message;
            if (ex.Message.Contains("session", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("owner_login", StringComparison.OrdinalIgnoreCase)) Lock();
        }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!int.TryParse(QuantityBox.Text, out var count) || count < 1 || count > 500)
        { OwnerStatus.Text = "Enter 1–500 keys."; return; }
        try
        {
            _busy = true;
            using var result = await SendAsync(HttpMethod.Post, "v1/admin/licenses/bulk", new { quantity = count, maxDevices = 1, note = "Created from Zuro" });
            var data = JsonSerializer.Deserialize<KeyList>(result.RootElement.GetRawText(), JsonOptions)?.Licenses ?? [];
            GeneratedKeys.Text = string.Join(Environment.NewLine, data.Select(k => k.Key));
            OwnerStatus.Text = $"Created {data.Count:N0} real license keys in Neon. Copy them now; raw keys are shown only once.";
            await RefreshAsync();
        }
        catch (Exception ex) { OwnerStatus.Text = ex.Message; }
        finally { _busy = false; }
    }
    private void CopyKeys_Click(object sender, RoutedEventArgs e)
    {
        if (GeneratedKeys.Text.StartsWith("ZORA-", StringComparison.OrdinalIgnoreCase))
        { Clipboard.SetText(GeneratedKeys.Text); OwnerStatus.Text = "Keys copied. Keep them private."; }
        else OwnerStatus.Text = "Generate keys first.";
    }
    private async Task ChangeStatusAsync(string status)
    {
        if (_busy || LicensesGrid.SelectedItem is not LicenseRow selected) { OwnerStatus.Text = "Select a license account first."; return; }
        var confirmation = MessageBox.Show($"Set {selected.KeyLabel} to {status}?", "Zuro license management", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes) return;
        try
        {
            _busy = true;
            using var result = await SendAsync(HttpMethod.Post, $"v1/admin/licenses/{selected.Id}/status", new { status });
            await RefreshAsync(); OwnerStatus.Text = $"License updated: {status}.";
        }
        catch (Exception ex) { OwnerStatus.Text = ex.Message; }
        finally { _busy = false; }
    }
    private async void Ban_Click(object sender, RoutedEventArgs e) => await ChangeStatusAsync("banned");
    private async void Unban_Click(object sender, RoutedEventArgs e) => await ChangeStatusAsync("active");
    private async void Revoke_Click(object sender, RoutedEventArgs e) => await ChangeStatusAsync("revoked");
    private void Lock()
    {
        _ownerToken = null;
        OwnerPassword.Clear();
        GeneratedKeys.Clear();
        _licenses.Clear();
        OwnerDashboard.Visibility = Visibility.Collapsed;
        OwnerLoginPanel.Visibility = Visibility.Visible;
    }
    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        try { if (_ownerToken is not null) using (await SendAsync(HttpMethod.Post, "v1/admin/logout")) { } }
        catch { /* Clear local token even if remote logout is unavailable. */ }
        finally { Lock(); OwnerStatus.Text = "Owner workspace locked."; }
    }
}
