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
    private string _pollFor = "discord";
    private bool _busy;

    public DiscordPanel()
    {
        InitializeComponent();
        _poll.Tick += async (_, _) =>
        {
            if (_busy || _license is null) return;
            if (--_remainingPolls < 0) { _poll.Stop(); if (_pollFor == "zuzify") ZuzifyFeedback.Text = "Zuzify link not confirmed. Refresh after authorizing."; else Feedback.Text = "Discord link not confirmed. Refresh after authorizing."; return; }
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
            if (result.Linked) { if (_pollFor == "discord") _poll.Stop(); Feedback.Text = "Verified Discord identity stored in your Neon account."; }
            try
            {
                var zuzify = await _license.GetZuzifyAsync();
                ZuzifyLinkState.Text = zuzify.Linked ? "CONNECTED" : "NOT LINKED";
                ZuzifyId.Text = zuzify.Zuzify is null ? "No Zuzify ID linked to this license." : $"Verified Zuzify ID: {zuzify.Zuzify.UserId}";
                ZuzifyConnectButton.Visibility = zuzify.Linked ? Visibility.Collapsed : Visibility.Visible;
                ZuzifyUnlinkButton.Visibility = zuzify.Linked ? Visibility.Visible : Visibility.Collapsed;
                if (zuzify.Linked && _pollFor == "zuzify") { _poll.Stop(); ZuzifyFeedback.Text = "Zuzify ID linked successfully."; }
            }
            catch (Exception ex) { if (showErrors) ZuzifyFeedback.Text = ex.Message; }
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
            _pollFor = "discord";
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

    private async void ZuzifyConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_license is null || _busy) return;
        _busy = true;
        ZuzifyConnectButton.IsEnabled = false;
        try
        {
            var response = await _license.StartZuzifyAsync();
            if (!Uri.TryCreate(response.Url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(uri.Host, "ep-rough-recipe-bcjp6wq7.neonauth.c-16.us-east-1.aws.neon.tech", StringComparison.OrdinalIgnoreCase) ||
                !uri.AbsolutePath.EndsWith("/oauth2/authorize", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected Zuzify authorization address.");
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
            ZuzifyFeedback.Text = "Approve Zora on Zuzify in the browser. Connection status will update.";
            _pollFor = "zuzify";
            _remainingPolls = 70;
            _poll.Start();
        }
        catch (Exception ex) { ZuzifyFeedback.Text = ex.Message; }
        finally { _busy = false; ZuzifyConnectButton.IsEnabled = true; }
    }

    private async void ZuzifyUnlink_Click(object sender, RoutedEventArgs e)
    {
        if (_license is null || _busy) return;
        if (MessageBox.Show("Disconnect this Zuzify ID from the Zora license?", "Zora account", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _busy = true;
        try
        {
            await _license.UnlinkZuzifyAsync();
            ZuzifyFeedback.Text = "Zuzify ID disconnected from this license.";
        }
        catch (Exception ex) { ZuzifyFeedback.Text = ex.Message; }
        finally { _busy = false; await RefreshStatusAsync(showErrors: true); }
    }
}
