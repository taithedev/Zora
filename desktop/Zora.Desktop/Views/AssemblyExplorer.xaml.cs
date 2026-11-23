using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using Microsoft.Win32;
using Zora.Desktop.Services;

namespace Zora.Desktop.Views;
public partial class AssemblyExplorer : UserControl
{
    private AssemblyInspector.AssemblyInfo? _assembly;
    private readonly TextEditor _code;
    private bool _exporting;
    private int _selectionVersion;

    public AssemblyExplorer()
    {
        InitializeComponent();
        _code = new TextEditor
        {
            Background = new SolidColorBrush(Color.FromRgb(9, 9, 9)),
            Foreground = Brushes.White,
            LineNumbersForeground = Brushes.Gray,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 13,
            IsReadOnly = true,
            ShowLineNumbers = true,
            WordWrap = false,
            Padding = new Thickness(16),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = "// Open a managed .NET assembly to view reconstructed C# source.\n// Native C++ or encrypted assemblies cannot be decompiled as ordinary .NET code."
        };
        try
        {
            var syntax = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("C#");
            if (syntax is not null) _code.SyntaxHighlighting = syntax;
        }
        catch { /* Plain text is still readable. */ }
        CodeHost.Children.Add(_code);
    }

    public async Task OpenAssemblyAsync(string path)
    {
        try
        {
            StatusText.Text = "Reading managed metadata...";
            _assembly = null;
            AssemblyTree.Items.Clear();
            ExportButton.IsEnabled = false;
            var info = await Task.Run(() => AssemblyInspector.Inspect(path));
            _assembly = info;
            AssemblyName.Text = info.Name;
            ExportButton.IsEnabled = true;
            RebuildTree();
            StatusText.Text = $"{info.Types.Count:N0} types found in {info.Name}. Select a class or method to view C#.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            AssemblyName.Text = "Could not load assembly";
            _code.Text = "// " + ex.Message;
        }
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Open a managed .NET assembly", Filter = "Assemblies|*.dll;*.exe|All files|*.*" };
        if (dialog.ShowDialog() == true) await OpenAssemblyAsync(dialog.FileName);
    }
    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (AssemblyTree is not null && _assembly is not null) RebuildTree();
    }
    private void RebuildTree()
    {
        if (_assembly is null) return;
        AssemblyTree.Items.Clear();
        var query = SearchBox.Text.Trim();
        foreach (var group in _assembly.Types
            .Where(type => query.Length == 0 || type.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || _assembly.Members[type.FullTypeName].Any(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(type => type.FullTypeName.Contains('.') ? type.FullTypeName[..type.FullTypeName.LastIndexOf('.')] : "(global)")
            .OrderBy(group => group.Key))
        {
            var namespaceNode = new TreeViewItem { Header = group.Key, Foreground = Brushes.White };
            foreach (var type in group.OrderBy(type => type.Name))
            {
                var typeNode = new TreeViewItem { Header = type.Name.Split('.').Last(), Tag = type, Foreground = Brushes.White };
                foreach (var member in _assembly.Members[type.FullTypeName].Where(member =>
                    query.Length == 0 || type.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || member.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
                    typeNode.Items.Add(new TreeViewItem { Header = member.Kind + " · " + member.Name,
                        Tag = member, Foreground = Brushes.LightGray });
                namespaceNode.Items.Add(typeNode);
            }
            AssemblyTree.Items.Add(namespaceNode);
        }
    }
    private async void AssemblyTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_assembly is null || (e.NewValue as TreeViewItem)?.Tag is not AssemblyInspector.Symbol symbol) return;
        int current = ++_selectionVersion;
        string file = _assembly.Path;
        try
        {
            StatusText.Text = "Decompiling " + symbol.FullTypeName + "...";
            var code = await Task.Run(() => AssemblyInspector.DecompileType(file, symbol.FullTypeName));
            if (current != _selectionVersion) return;
            _code.Text = code;
            if (symbol.MemberName is { Length: > 0 })
            {
                int line = 1;
                var offset = code.IndexOf(symbol.MemberName, StringComparison.Ordinal);
                if (offset >= 0) line += code[..offset].Count(c => c == '\n');
                _code.ScrollToLine(line);
            }
            StatusText.Text = "C# preview · " + symbol.FullTypeName + " · decompiled, not original source";
        }
        catch (Exception ex) { if (current == _selectionVersion) StatusText.Text = "Cannot decompile this type: " + ex.Message; }
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_assembly is null || _exporting) return;
        var dialog = new OpenFolderDialog { Title = "Choose folder for decompiled .NET C# files" };
        if (dialog.ShowDialog() != true) return;
        var info = _assembly;
        try
        {
            _exporting = true;
            ExportButton.IsEnabled = false;
            StatusText.Text = "Decompiling all available types...";
            var result = await Task.Run(() => AssemblyInspector.ExportCSharp(info, dialog.FolderName));
            StatusText.Text = $"Saved {result.Exported:N0} C# files; {result.Errors.Count:N0} warnings. See ZORA_DECOMPILE_REPORT.txt.";
        }
        catch (Exception ex) { StatusText.Text = "Export failed: " + ex.Message; }
        finally { _exporting = false; ExportButton.IsEnabled = _assembly is not null; }
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_code.Text)) { StatusText.Text = "No C# source to copy"; return; }
        try { Clipboard.SetText(_code.Text); StatusText.Text = "Decompiled C# copied"; }
        catch (Exception ex) { StatusText.Text = "Copy failed: " + ex.Message; }
    }
}
