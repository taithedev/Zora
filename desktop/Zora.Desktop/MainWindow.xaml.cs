using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Zora.Desktop.Models;
using Zora.Desktop.Services;

namespace Zora.Desktop;
public partial class MainWindow : Window
{
    private readonly EngineService _engine = new();
    private readonly LicenseService _license = new();
    private readonly ObservableCollection<AssetRow> _assets = [];
    private string? _source;
    private bool _busy;
    public MainWindow()
    {
        InitializeComponent();
        AssetGrid.ItemsSource = _assets;
        SourceInitialized += (_, _) => {
            try { var handle = new WindowInteropHelper(this).Handle; int dark = 1; DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)); }
            catch { /* Dark title bars are optional on older Windows. */ }
        };
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Checking license…";
        if (!await _license.RefreshAsync())
        {
            var prompt = new LicenseWindow(_license) { Owner = this };
            if (prompt.ShowDialog() != true) { Close(); return; }
        }
        AccountLabel.Text = _license.Username;
        StatusText.Text = "License active · Ready";
    }
    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Select a Unity project folder (with Assets and ProjectSettings)" };
        if (picker.ShowDialog() == true) await LoadAsync(picker.FolderName);
    }
    private async void OpenBuild_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog {
            Title = "Select Unity game EXE, APK, or serialized asset",
            Filter = "Unity games and assets|*.exe;*.apk;*.assets;*.bundle;*.unity3d;*.assetbundle;*.ab|All files|*.*"
        };
        if (picker.ShowDialog() == true) await LoadAsync(picker.FileName);
    }
    private async Task LoadAsync(string source)
    {
        if (_busy) return;
        try
        {
            Busy(true, "Indexing assets…");
            _assets.Clear();
            _source = source;
            SourceLabel.Text = source;
            var scan = await _engine.ScanAsync(source);
            foreach (var row in scan.Assets) _assets.Add(row);
            RefreshFilter();
            StatusText.Text = scan.Warnings.Count > 0 ? $"Indexed · {scan.Warnings.Count} warnings" : "Indexed successfully";
            if (scan.Warnings.Count > 0) MessageBox.Show(string.Join("\n", scan.Warnings.Take(8)), "Zora import warnings", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { StatusText.Text = "Import failed"; MessageBox.Show(ex.Message, "Zora import failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
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
                4 => asset.Type is "TextAsset" or "MonoBehaviour" or "Shader" or "Source",
                5 => asset.Type is not ("Texture2D" or "Sprite" or "Texture" or "Mesh" or "Model" or "AudioClip" or "Audio" or "TextAsset" or "MonoBehaviour" or "Shader" or "Source"),
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
            DetailName.Text = "No selection"; DetailType.Text = DetailOrigin.Text = DetailSize.Text = DetailSource.Text = "";
            PreviewImage.Visibility = Visibility.Collapsed; NoPreviewText.Visibility = Visibility.Visible; return;
        }
        DetailName.Text = row.Name;
        DetailType.Text = "Type: " + row.Type + (row.Exportable ? "" : " · metadata only");
        DetailOrigin.Text = "Origin: " + row.Origin;
        DetailSize.Text = "Size: " + row.SizeText;
        DetailSource.Text = "Source: " + row.Source;
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
        catch { /* Some textures require conversion by the extractor. */ }
    }
    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null || AssetGrid.SelectedItem is not AssetRow row) return;
        if (row.Type is not ("Texture2D" or "Sprite" or "Texture")) { StatusText.Text = "Select a texture or sprite"; return; }
        if (!await _license.VerifyAsync()) { StatusText.Text = "Unable to verify license"; return; }
        try
        {
            Busy(true, "Rendering preview…");
            var temp = Path.Combine(Path.GetTempPath(), "Zora", "preview", Guid.NewGuid().ToString("N"));
            var result = await _engine.ExportAsync(_source, temp, [row.Id]);
            var png = result.Exported.FirstOrDefault(p => Path.GetExtension(p).Equals(".png", StringComparison.OrdinalIgnoreCase));
            if (png is not null) ShowPreview(png);
            else StatusText.Text = result.Warnings.FirstOrDefault() ?? "Preview unavailable for this texture";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { Busy(false); }
    }
    private async void ExportSelected_Click(object sender, RoutedEventArgs e)
    {
        var ids = AssetGrid.SelectedItems.Cast<AssetRow>().Where(r => r.Exportable).Select(r => r.Id).ToArray();
        if (ids.Length == 0) { StatusText.Text = "Select one or more exportable assets"; return; }
        await ExportAsync(ids);
    }
    private async void ExportAll_Click(object sender, RoutedEventArgs e)
    {
        await ExportAsync(null);
    }
    private async Task ExportAsync(string[]? ids)
    {
        if (_busy || _source is null) { StatusText.Text = "Open a Unity source first"; return; }
        if (!await _license.VerifyAsync()) { StatusText.Text = "License verification required"; MessageBox.Show("Could not verify your license. Check the API connection and license status.", "Zora", MessageBoxButton.OK); return; }
        var picker = new OpenFolderDialog { Title = "Select an empty output folder for extracted assets" };
        if (picker.ShowDialog() != true) return;
        try
        {
            Busy(true, "Exporting files…");
            var result = await _engine.ExportAsync(_source, picker.FolderName, ids);
            StatusText.Text = $"Exported {result.Count:N0} files";
            MessageBox.Show($"Exported {result.Count:N0} files to:\n{picker.FolderName}\n\nWarnings: {result.Warnings.Count}" +
                            (result.Warnings.Count > 0 ? "\n\n" + string.Join("\n", result.Warnings.Take(6)) : ""),
                "Zora export complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { StatusText.Text = "Export failed"; MessageBox.Show(ex.Message, "Zora export failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { Busy(false); }
    }
    private void Busy(bool busy, string? message = null)
    {
        _busy = busy;
        ExportSelectedButton.IsEnabled = ExportAllButton.IsEnabled = !busy;
        if (message is not null) StatusText.Text = message;
    }
}
