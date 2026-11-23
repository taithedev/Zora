using System.IO;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Zora.Desktop.Models;
using Zora.Desktop.Services;
using Zora.Updates;

namespace Zora.Desktop;
public partial class MainWindow : Window
{
    private readonly EngineService _engine = new();
    private readonly LicenseService _license = new();
    private readonly UpdateService _updates = new("Zora-Windows-x64.zip", "Zora.exe");
    private bool _checkingUpdates;
    private readonly ObservableCollection<AssetRow> _assets = [];
    private string? _source;
    private bool _busy;
    public MainWindow()
    {
        InitializeComponent();
        AssetGrid.ItemsSource = _assets;
        DiscordView.UseLicense(_license);
        SourceInitialized += (_, _) => {
            try { var handle = new WindowInteropHelper(this).Handle; int dark = 1; DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)); int round = 2; DwmSetWindowAttribute(handle, 33, ref round, sizeof(int)); }
            catch { /* Native dark title-bar attribute may be unsupported. */ }
        };
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ActivationOverlay.Visibility = Visibility.Visible;
        WorkspaceRoot.Visibility = Visibility.Collapsed;
        ActivationStatus.Text = "";
        if (await _license.RefreshAsync()) EnterWorkspace();
        else LicenseKeyBox.Focus();
    }
    private void EnterWorkspace()
    {
        AccountLabel.Text = _license.Username;
        SessionStat.Text = "ACTIVE";
        ActivationOverlay.Visibility = Visibility.Collapsed;
        WorkspaceRoot.Visibility = Visibility.Visible;
        StatusText.Text = "License verified · Ready";
        SwitchPage("explorer");
        _ = CheckUpdatesAsync(false);
    }
    private void KeyNext_Click(object sender, RoutedEventArgs e)
    {
        var key = LicenseKeyBox.Text.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(key, @"^ZORA-(?:[A-HJ-NP-Z2-9]{5}-){4}[A-HJ-NP-Z2-9]{5}$"))
        {
            ActivationStatus.Text = "Enter a valid ZORA license key.";
            return;
        }
        ActivationStatus.Text = "";
        KeyEntryPanel.Visibility = Visibility.Collapsed;
        UsernameEntryPanel.Visibility = Visibility.Visible;
        UsernameBox.Focus();
    }
    private void ActivationBack_Click(object sender, RoutedEventArgs e)
    {
        UsernameEntryPanel.Visibility = Visibility.Collapsed;
        KeyEntryPanel.Visibility = Visibility.Visible;
        ActivationStatus.Text = "";
        LicenseKeyBox.Focus();
    }
    private void LicenseKey_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Return) KeyNext_Click(sender, e); }
    private void Username_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Return) Activate_Click(sender, e); }
    private async void Activate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ActivateButton.IsEnabled = false;
            ActivationStatus.Text = "Checking license...";
            await _license.ActivateAsync(UsernameBox.Text.Trim(), LicenseKeyBox.Text.Trim());
            LicenseKeyBox.Clear();
            EnterWorkspace();
        }
        catch (Exception ex) { ActivationStatus.Text = ex.Message; }
        finally { ActivateButton.IsEnabled = true; }
    }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { Maximize_Click(sender, e); return; }
        if (e.OriginalSource is DependencyObject d && FindParentButton(d)) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }
    private static bool FindParentButton(DependencyObject? obj)
    {
        while (obj is not null)
        {
            if (obj is Button) return true;
            obj = System.Windows.Media.VisualTreeHelper.GetParent(obj);
        }
        return false;
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!CodeStudioView.ConfirmExit()) e.Cancel = true;
    }
    private void Explorer_Click(object sender, RoutedEventArgs e) => SwitchPage("explorer");
    private void CodeStudio_Click(object sender, RoutedEventArgs e) => SwitchPage("editor");
    private void Zuro_Click(object sender, RoutedEventArgs e) => SwitchPage("zuro");
    private void Discord_Click(object sender, RoutedEventArgs e) => SwitchPage("discord");
    private void SwitchPage(string page)
    {
        ExplorerBody.Visibility = page == "explorer" ? Visibility.Visible : Visibility.Collapsed;
        InspectorBody.Visibility = page == "explorer" ? Visibility.Visible : Visibility.Collapsed;
        CodeStudioView.Visibility = page == "editor" ? Visibility.Visible : Visibility.Collapsed;
        ZuroView.Visibility = page == "zuro" ? Visibility.Visible : Visibility.Collapsed;
        DiscordView.Visibility = page == "discord" ? Visibility.Visible : Visibility.Collapsed;
        var active = page == "editor" ? (FrameworkElement)CodeStudioView :
                     page == "zuro" ? ZuroView :
                     page == "discord" ? DiscordView : ExplorerBody;
        active.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(230))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        var offset = new TranslateTransform(0, 12);
        active.RenderTransform = offset;
        offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(260))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        if (page == "editor") CodeStudioView.FocusEditor();
        if (page == "explorer") StatusText.Text = _source is null ? "Open a project or application" : "Asset explorer";
    }
    private void AssetGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_source is null || !Directory.Exists(_source) || AssetGrid.SelectedItem is not AssetRow row || row.Origin != "Project") return;
        try
        {
            var root = Path.GetFullPath(_source);
            var file = Path.GetFullPath(Path.Combine(root, row.Source.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !CodeStudioView.CanOpen(file)) return;
            CodeStudioView.OpenFile(file);
            SwitchPage("editor");
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (ActivationOverlay.Visibility == Visibility.Visible || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (paths is { Length: > 0 }) await LoadAsync(paths[0]);
    }
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = ActivationOverlay.Visibility != Visibility.Visible && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Select Unity project folder" };
        if (picker.ShowDialog() == true) await LoadAsync(picker.FolderName);
    }
    private async void OpenBuild_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog {
            Title = "Select application, APK, DLL, archive or Unity assets",
            Filter = "Apps and packages|*.exe;*.dll;*.apk;*.aab;*.jar;*.zip;*.msix;*.nupkg;*.assets;*.bundle;*.unity3d;*.assetbundle;*.ab|All files|*.*"
        };
        if (picker.ShowDialog() == true) await LoadAsync(picker.FileName);
    }
    private async Task LoadAsync(string source)
    {
        if (_busy) return;
        try
        {
            Busy(true, "Indexing assets...");
            _assets.Clear();
            _source = null;
            SourceLabel.Text = source;
            var scan = await _engine.ScanAsync(source);
            _source = source;
            foreach (var row in scan.Assets) _assets.Add(row);
            IndexedStat.Text = _assets.Count.ToString("N0");
            SourceStat.Text = Directory.Exists(source) && Directory.Exists(Path.Combine(source, "ProjectSettings")) ? "PROJECT" :
                Path.GetExtension(source).Equals(".apk", StringComparison.OrdinalIgnoreCase) ? "APK" :
                Path.GetExtension(source).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? "EXE" :
                Path.GetExtension(source).Equals(".dll", StringComparison.OrdinalIgnoreCase) ? "DLL" :
                new[] { ".zip", ".jar", ".msix", ".aab", ".nupkg" }.Contains(Path.GetExtension(source).ToLowerInvariant()) ? "ARCHIVE" : "ASSETS";
            RefreshFilter();
            NoticeLabel.Text = scan.Warnings.Count == 0 ? "Source indexed. EXPORT ALL saves readable assets; RECOVER UNITY PROJECT is available only for Unity projects/builds." :
                $"{scan.Warnings.Count} files could not be parsed. {scan.Warnings[0]}";
            StatusText.Text = scan.Warnings.Count > 0 ? $"Indexed · {scan.Warnings.Count} warnings" : "Source indexed";
        }
        catch (Exception ex) { StatusText.Text = "Import failed"; NoticeLabel.Text = ex.Message; }
        finally { Busy(false); }
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) => RefreshFilter();
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => RefreshFilter();
    private void RefreshFilter()
    {
        if (AssetGrid is null || TypeFilter is null || SearchBox is null) return;
        var view = CollectionViewSource.GetDefaultView(AssetGrid.ItemsSource);
        if (view is null) return;
        var search = SearchBox.Text.Trim();
        var type = TypeFilter.SelectedIndex;
        view.Filter = row =>
        {
            if (row is not AssetRow asset) return false;
            if (search.Length > 0 && !asset.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !asset.Type.Contains(search, StringComparison.OrdinalIgnoreCase) && !asset.Source.Contains(search, StringComparison.OrdinalIgnoreCase)) return false;
            return type switch {
                1 => asset.Type is "Texture2D" or "Sprite" or "Texture",
                2 => asset.Type is "Mesh" or "Model",
                3 => asset.Type is "AudioClip" or "Audio",
                4 => asset.Type is "TextAsset" or "MonoBehaviour" or "Shader" or "Source" or "Material/Data" or "Scene/Prefab",
                5 => asset.Type is not ("Texture2D" or "Sprite" or "Texture" or "Mesh" or "Model" or "AudioClip" or "Audio" or "TextAsset" or "MonoBehaviour" or "Shader" or "Source" or "Material/Data" or "Scene/Prefab"),
                6 => asset.Origin is "Archive" or "Executable",
                7 => asset.Type == "PE Section",
                _ => true
            };
        };
        view.Refresh();
        AssetCountText.Text = $"{_assets.Count:N0} indexed · {view.Cast<object>().Count():N0} visible";
    }
    private void AssetGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AssetGrid.SelectedItem is not AssetRow row)
        {
            DetailName.Text = "Nothing selected"; DetailType.Text = DetailOrigin.Text = DetailSize.Text = DetailSource.Text = "";
            PreviewImage.Visibility = Visibility.Collapsed; NoPreviewText.Visibility = Visibility.Visible; return;
        }
        DetailName.Text = row.Name;
        DetailType.Text = "TYPE  /  " + row.Type + (row.Exportable ? "" : "  /  METADATA ONLY");
        DetailOrigin.Text = "ORIGIN  /  " + row.Origin;
        DetailSize.Text = "SIZE  /  " + row.SizeText;
        DetailSource.Text = "PATH  /  " + row.Source;
        PreviewImage.Visibility = Visibility.Collapsed; NoPreviewText.Visibility = Visibility.Visible;
        if (row.Origin == "Project" && _source is not null && row.Type == "Texture")
            ShowPreview(Path.Combine(_source, row.Source.Replace('/', Path.DirectorySeparatorChar)));
    }
    private void ShowPreview(string file)
    {
        if (!File.Exists(file) || !new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" }.Contains(Path.GetExtension(file).ToLowerInvariant())) return;
        try
        {
            var img = new BitmapImage(); img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.UriSource = new Uri(file); img.EndInit(); img.Freeze();
            PreviewImage.Source = img; PreviewImage.Visibility = Visibility.Visible; NoPreviewText.Visibility = Visibility.Collapsed;
        }
        catch { /* Some compressed texture formats need conversion. */ }
    }
    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null || AssetGrid.SelectedItem is not AssetRow row) return;
        if (row.Type is not ("Texture2D" or "Sprite" or "Texture")) { StatusText.Text = "Select a texture or sprite"; return; }
        if (!await _license.VerifyAsync()) { RequireLicense(); return; }
        try
        {
            Busy(true, "Rendering preview...");
            var temp = Path.Combine(Path.GetTempPath(), "Zora", "preview", Guid.NewGuid().ToString("N"));
            var result = await _engine.ExportAsync(_source, temp, [row.Id]);
            var png = result.Exported.FirstOrDefault(p => Path.GetExtension(p).Equals(".png", StringComparison.OrdinalIgnoreCase));
            if (png is not null) ShowPreview(png);
            else StatusText.Text = result.Warnings.FirstOrDefault() ?? "Preview unavailable for this texture";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { Busy(false); }
    }
    private void RequireLicense()
    {
        SessionStat.Text = "LOCKED";
        WorkspaceRoot.Visibility = Visibility.Collapsed;
        ActivationOverlay.Visibility = Visibility.Visible;
        UsernameEntryPanel.Visibility = Visibility.Collapsed;
        KeyEntryPanel.Visibility = Visibility.Visible;
        ActivationStatus.Text = "License validation failed. Enter a valid license key.";
        LicenseKeyBox.Focus();
    }
    private async void ExportSelected_Click(object sender, RoutedEventArgs e)
    {
        var ids = AssetGrid.SelectedItems.Cast<AssetRow>().Where(r => r.Exportable).Select(r => r.Id).ToArray();
        if (ids.Length == 0) { StatusText.Text = "Select one or more exportable assets"; return; }
        await ExportAsync(ids);
    }
    private async void ExportAll_Click(object sender, RoutedEventArgs e) => await ExportAsync(null);
    private async Task ExportAsync(string[]? ids)
    {
        if (_busy || _source is null) { StatusText.Text = "Open a Unity source first"; return; }
        if (!await _license.VerifyAsync()) { RequireLicense(); return; }
        var picker = new OpenFolderDialog { Title = "Choose output folder for exported assets" };
        if (picker.ShowDialog() != true) return;
        try
        {
            Busy(true, "Exporting assets...");
            var result = await _engine.ExportAsync(_source, picker.FolderName, ids);
            StatusText.Text = $"Exported {result.Count:N0} assets";
            NoticeLabel.Text = $"Export completed to {picker.FolderName}. Warnings: {result.Warnings.Count}." +
                               (result.Warnings.Count > 0 ? " " + result.Warnings[0] : "");
        }
        catch (Exception ex) { StatusText.Text = "Export failed"; NoticeLabel.Text = ex.Message; }
        finally { Busy(false); }
    }
    private async void RecoverProject_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _source is null) { StatusText.Text = "Open a Unity EXE, APK, or project first"; return; }
        if (!await _license.VerifyAsync()) { RequireLicense(); return; }
        var picker = new OpenFolderDialog { Title = "Choose an empty folder for the recovered Unity project" };
        if (picker.ShowDialog() != true) return;
        try
        {
            Busy(true, "Recovering Unity project...");
            var result = await _engine.RecoverAsync(_source, picker.FolderName);
            StatusText.Text = $"Recovered {result.Count:N0} files";
            NoticeLabel.Text = $"Unity project created at {result.ProjectPath}. {result.Warnings.Count} warnings. " +
                (result.Warnings.Count > 0 ? result.Warnings[0] : "Read RECOVERY_REPORT.json for recovery limits.");
        }
        catch (Exception ex) { StatusText.Text = "Recovery failed"; NoticeLabel.Text = ex.Message; }
        finally { Busy(false); }
    }
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await CheckUpdatesAsync(true);
    private async Task CheckUpdatesAsync(bool manual)
    {
        if (_checkingUpdates) return;
        _checkingUpdates = true;
        CheckUpdatesButton.IsEnabled = false;
        try
        {
            if (manual) StatusText.Text = "Checking official release metadata...";
            var update = await _updates.CheckAndStageAsync();
            if (update is null)
            {
                if (manual) StatusText.Text = "Zora is up to date";
                return;
            }
            var answer = MessageBox.Show(this, $"Zora {update.Version} was downloaded and SHA-256 verified. Restart to install now?",
                "Zora update ready", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes)
            {
                _updates.ApplyAndRestart(update);
                Close();
            }
            else StatusText.Text = $"Version {update.Version} staged. Use Check for updates to install.";
        }
        catch (Exception ex)
        {
            if (manual) MessageBox.Show(this, ex.Message, "Update unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally { _checkingUpdates = false; CheckUpdatesButton.IsEnabled = true; }
    }
    private void Busy(bool busy, string? message = null)
    {
        _busy = busy;
        ExportSelectedButton.IsEnabled = ExportAllButton.IsEnabled = RecoverButton.IsEnabled = !busy;
        WorkingBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (message is not null) StatusText.Text = message;
    }
}
