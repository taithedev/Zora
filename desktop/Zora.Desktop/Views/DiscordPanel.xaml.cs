using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Zora.Desktop.Services;

namespace Zora.Desktop.Views;

public partial class DiscordPanel : UserControl
{
    private LicenseService? _license;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(4) };
    private int _remainingPolls;
    private bool _busy;

    public DiscordPanel()
    {
        InitializeComponent();
        _poll.Tick += async (_, _) =>
        {
            if (_busy || _license is null) return;
            if (--_remainingPolls < 0) { _poll.Stop(); Feedback.Text = "Link not confirmed yet. Use Refresh status after authorizing in Discord."; return; }
            await RefreshStatusAsync(showErrors: false);
        };
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible && _license is not null) await RefreshStatusAsync(showErrors: false);
            else _poll.Stop();
        };
    }
    public void UseLicense(LicenseService license) => _license = license;

    private async Task RefreshStatusAsync(bool showErrors)
    {
        if (_busy || _license is null) return;
        _busy = true;
        RefreshButton.IsEnabled = false;
        try
        {
            var result = await _license.GetDiscordAsync();
            LinkState.Text = result.Linked ? "CONNECTED" : "NOT LINKED";
            DiscordName.Text = result.Discord?.DisplayName ?? result.Discord?.Username ?? "No Discord account connected";
            DiscordId.Text = result.Discord is null ? "No account linked to your Zora license yet." :
                $"@{result.Discord.Username} · Discord ID: {result.Discord.Id}";
            ConnectButton.Visibility = result.Linked ? Visibility.Collapsed : Visibility.Visible;
            UnlinkButton.Visibility = result.Linked ? Visibility.Visible : Visibility.Collapsed;
            if (result.Linked) { _poll.Stop(); Feedback.Text = "Verified Discord identity stored in your Neon account."; }
        }
        catch (Exception e) { if (showErrors) Feedback.Text = e.Message; }
        finally { _busy = false; RefreshButton.IsEnabled = true; }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshStatusAsync(showErrors: true);
    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_license is null || _busy) return;
        _busy = true;
        ConnectButton.IsEnabled = false;
        try
        {
            Feedback.Text = "Opening Discord authorization…";
            var response = await _license.StartDiscordAsync();
            if (!Uri.TryCreate(response.Url, UriKind.Absolute, out var uri) ||
                uri.Scheme != "https" || !string.Equals(uri.Host, "discord.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected Discord authorization address.");
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
            Feedback.Text = "Approve Zora on Discord in your browser. This page will update when linked.";
            _remainingPolls = 70;
            _poll.Start();
        }
        catch (Exception ex) { Feedback.Text = ex.Message; }
        finally { _busy = false; ConnectButton.IsEnabled = true; }
    }
    private async void Unlink_Click(object sender, RoutedEventArgs e)
    {
        if (_license is null || _busy) return;
        if (MessageBox.Show("Disconnect Discord from this Zora license?", "Zora account", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _busy = true;
        try
        {
            await _license.UnlinkDiscordAsync();
            Feedback.Text = "Your Discord identity was removed from this license.";
        }
        catch (Exception ex) { Feedback.Text = ex.Message; }
        finally { _busy = false; await RefreshStatusAsync(showErrors: true); }
    }
}
