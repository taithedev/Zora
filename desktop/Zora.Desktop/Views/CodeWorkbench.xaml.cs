using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.Win32;

namespace Zora.Desktop.Views;

public partial class CodeWorkbench : UserControl
{
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private static readonly string[] Extensions = [".cs", ".lua", ".py", ".js", ".jsx", ".ts", ".tsx", ".json", ".xml", ".xaml", ".html", ".css", ".scss", ".shader", ".hlsl", ".cginc", ".compute", ".txt", ".md", ".yaml", ".yml", ".sql", ".csproj", ".props", ".targets", ".config", ".unity", ".prefab", ".asset"];
    private static readonly string[] Keywords = ["class", "public", "private", "protected", "internal", "static", "void", "return", "using", "namespace", "interface", "struct", "record", "async", "await", "Task", "string", "int", "float", "bool", "double", "decimal", "null", "true", "false", "new", "this", "base", "override", "virtual", "readonly", "const", "if", "else", "for", "foreach", "while", "switch", "case", "break", "continue", "try", "catch", "finally", "throw", "get", "set", "var", "List", "Dictionary", "GameObject", "Transform", "Vector3", "Quaternion", "MonoBehaviour", "Start", "Update", "FixedUpdate", "SerializeField", "Debug", "Log", "GetComponent", "Instantiate", "Destroy", "function", "local", "end", "then", "import", "from", "def", "self", "print", "yield", "document", "window", "let", "const", "console", "export", "default"];
    private sealed class EditorTab
    {
        public required TabItem Tab { get; init; }
        public required TextEditor Editor { get; init; }
        public string? Path { get; set; }
        public bool Dirty { get; set; }
        public string Title => Path is null ? "untitled" : System.IO.Path.GetFileName(Path);
    }
    private readonly List<EditorTab> _tabs = [];
    private CompletionWindow? _completion;
    public CodeWorkbench()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshEmpty();
    }
    public void FocusEditor() => Current?.Editor.Focus();
    public bool CanOpen(string path) => File.Exists(path) && Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public bool ConfirmExit()
    {
        var count = _tabs.Count(tab => tab.Dirty);
        if (count == 0) return true;
        return MessageBox.Show($"Close Zora with {count} unsaved editor tab(s)? Changes will be lost.",
            "Unsaved source files", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
    private EditorTab? Current => Documents.SelectedItem is TabItem tab ? _tabs.FirstOrDefault(t => t.Tab == tab) : null;

    public void OpenFile(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        if (!CanOpen(path)) throw new InvalidOperationException("Only supported, readable source or text files can be opened in Code Studio.");
        if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidOperationException("Files larger than 5 MB cannot be opened in the editor.");
        var existing = _tabs.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { Documents.SelectedItem = existing.Tab; return; }
        var bytes = File.ReadAllBytes(path);
        if (Array.IndexOf(bytes, (byte)0) >= 0 && !path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This file looks binary. Use the asset inspector rather than the text editor.");
        // UTF-8 with BOM support; UTF-16 files are handled by StreamReader with BOM detection.
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var value = reader.ReadToEnd();
        AddTab(path, value);
    }
    private void AddTab(string? path, string text)
    {
        var editor = new TextEditor
        {
            FontFamily = new FontFamily("Cascadia Code, Consolas"), FontSize = 13.5,
            Background = new SolidColorBrush(Color.FromRgb(8, 8, 8)), Foreground = Brushes.White,
            LineNumbersForeground = Brushes.Gray, ShowLineNumbers = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = text, Padding = new Thickness(14), WordWrap = false
        };
        editor.Options.ConvertTabsToSpaces = true;
        editor.Options.IndentationSize = 4;
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        try
        {
            using var stream = typeof(CodeWorkbench).Assembly.GetManifestResourceStream("Zora.Desktop.Views.Monochrome.xshd");
            if (stream is null) throw new InvalidOperationException("Missing monochrome syntax theme");
            using var xml = XmlReader.Create(stream);
            editor.SyntaxHighlighting = HighlightingLoader.Load(xml, HighlightingManager.Instance);
        }
        catch { editor.SyntaxHighlighting = null; } // Always preserve editing if highlighting fails.
        var tab = new TabItem { Header = path is null ? "untitled" : System.IO.Path.GetFileName(path), Content = editor, Padding = new Thickness(17, 9, 17, 9) };
        var state = new EditorTab { Tab = tab, Editor = editor, Path = path };
        _tabs.Add(state);
        editor.TextChanged += (_, _) => { state.Dirty = true; UpdateHeader(state); UpdateStatus(); };
        editor.TextArea.TextEntered += (_, e) => OnTextEntered(editor, e);
        editor.TextArea.PreviewKeyDown += (_, e) => EditorKeyDown(editor, e);
        editor.TextArea.Caret.PositionChanged += (_, _) => UpdateStatus();
        Documents.Items.Add(tab);
        Documents.SelectedItem = tab;
        RefreshEmpty();
        editor.Focus();
    }
    private static bool WordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';
    private void OnTextEntered(TextEditor editor, TextCompositionEventArgs e)
    {
        if (e.Text.Length != 1 || !WordCharacter(e.Text[0])) return;
        var offset = editor.CaretOffset;
        var start = offset;
        while (start > 0 && WordCharacter(editor.Document.GetCharAt(start - 1))) start--;
        var prefix = editor.Document.GetText(start, offset - start);
        if (prefix.Length < 2) return;
        var matches = Keywords.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !k.Equals(prefix, StringComparison.OrdinalIgnoreCase)).Distinct().Take(16).ToArray();
        if (matches.Length > 0) ShowCompletions(editor, matches, start);
    }
    private void ShowCompletions(TextEditor editor, string[] matches, int start)
    {
        _completion?.Close();
        _completion = new CompletionWindow(editor.TextArea) { StartOffset = start };
        foreach (var item in matches) _completion.CompletionList.CompletionData.Add(new CodeSuggestion(item));
        _completion.Closed += (_, _) => _completion = null;
        _completion.Show();
    }
    private void EditorKeyDown(TextEditor editor, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            var at = editor.CaretOffset;
            var start = at;
            while (start > 0 && WordCharacter(editor.Document.GetCharAt(start-1))) start--;
            var prefix = editor.Document.GetText(start, at-start);
            var suggestions = Keywords.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Take(20).ToArray();
            if (suggestions.Length > 0) ShowCompletions(editor, suggestions, start);
            e.Handled = true;
        }
        else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
        {
            var line = editor.Document.GetLineByOffset(editor.CaretOffset);
            var original = editor.Document.GetText(line.Offset, line.Length);
            var newline = editor.Document.Text.Contains("\r\n") ? "\r\n" : "\n";
            editor.Document.Insert(line.EndOffset, newline + original);
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { SaveCurrent(false); e.Handled = true; }
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control) { Open_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { AddTab(null, ""); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { FindTextBox.Focus(); FindTextBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.Control && Documents.Items.Count > 0)
        { Documents.SelectedIndex = (Documents.SelectedIndex + 1) % Documents.Items.Count; e.Handled = true; }
    }
    private sealed class CodeSuggestion(string text) : ICompletionData
    {
        public System.Windows.Media.ImageSource? Image => null;
        public string Text => text;
        public object Content => text;
        public object Description => $"Insert {text}";
        public double Priority => 1;
        public void Complete(TextArea area, ISegment segment, EventArgs e) => area.Document.Replace(segment, text);
    }
    private void UpdateHeader(EditorTab tab) => tab.Tab.Header = tab.Title + (tab.Dirty ? "  •" : "");
    private void UpdateStatus()
    {
        var state = Current;
        if (state is null) { EditorStatus.Text = "No file open"; return; }
        var pos = state.Editor.TextArea.Caret;
        EditorStatus.Text = $"{state.Title}  ·  Ln {pos.Line}, Col {pos.Column}  ·  {state.Editor.Document.LineCount:N0} lines" + (state.Dirty ? "  ·  UNSAVED" : "");
    }
    private void RefreshEmpty() { if (EmptyState is not null) EmptyState.Visibility = _tabs.Count == 0 ? Visibility.Visible : Visibility.Collapsed; }
    private void New_Click(object sender, RoutedEventArgs e) => AddTab(null, "");
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Open a source file", Filter = "Source files|*.cs;*.lua;*.py;*.js;*.ts;*.json;*.xml;*.xaml;*.html;*.css;*.shader;*.hlsl;*.txt;*.md;*.yaml;*.yml;*.sql;*.unity;*.prefab;*.asset|All files|*.*", Multiselect = true };
        if (picker.ShowDialog() != true) return;
        foreach (var path in picker.FileNames)
        {
            try { OpenFile(path); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Code Studio", MessageBoxButton.OK, MessageBoxImage.Information); }
        }
    }
    private void Save_Click(object sender, RoutedEventArgs e) => SaveCurrent(false);
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveCurrent(true);
    private bool SaveCurrent(bool saveAs)
    {
        var state = Current;
        if (state is null) return false;
        var path = saveAs ? null : state.Path;
        if (path is null)
        {
            var picker = new SaveFileDialog { Title = "Save source", FileName = state.Path is null ? "untitled.cs" : System.IO.Path.GetFileName(state.Path), Filter = "Source files|*.cs;*.lua;*.py;*.js;*.json;*.txt;*.shader;*.xml;*.xaml;*.sql|All files|*.*" };
            if (picker.ShowDialog() != true) return false;
            path = picker.FileName;
        }
        try
        {
            // Write via same-directory temp file, replacing only when the write has completed.
            var tmp = path + ".zora-writing-" + Guid.NewGuid().ToString("N");
            try { File.WriteAllText(tmp, state.Editor.Text, new UTF8Encoding(false)); File.Move(tmp, path, true); }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
            state.Path = path;
            state.Dirty = false;
            UpdateHeader(state); UpdateStatus();
            return true;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Unable to save", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
    }
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        var state = Current;
        if (state is null) return;
        if (state.Dirty)
        {
            var answer = MessageBox.Show($"Save changes to {state.Title}?", "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;
            if (answer == MessageBoxResult.Yes && !SaveCurrent(false)) return;
        }
        _completion?.Close();
        _tabs.Remove(state);
        Documents.Items.Remove(state.Tab);
        RefreshEmpty(); UpdateStatus();
    }
    private void Find_Click(object sender, RoutedEventArgs e)
    {
        var editor = Current?.Editor;
        if (editor is null) return;
        var query = FindTextBox.Text;
        if (string.IsNullOrEmpty(query)) { FindTextBox.Focus(); return; }
        var index = editor.Text.IndexOf(query, editor.SelectionStart + editor.SelectionLength, StringComparison.OrdinalIgnoreCase);
        if (index < 0) index = editor.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0) { EditorStatus.Text = "No matches"; return; }
        editor.Select(index, query.Length); editor.ScrollToLine(editor.Document.GetLineByOffset(index).LineNumber); editor.Focus();
    }
    private void GoLine_Click(object sender, RoutedEventArgs e)
    {
        var editor = Current?.Editor;
        if (editor is null) return;
        var raw = GoLineBox.Text;
        if (string.IsNullOrEmpty(raw)) { GoLineBox.Text = editor.TextArea.Caret.Line.ToString(); GoLineBox.Focus(); GoLineBox.SelectAll(); return; }
        if (int.TryParse(raw, out var line) && line >= 1 && line <= editor.Document.LineCount)
        { editor.TextArea.Caret.Line = line; editor.TextArea.Caret.Column = 1; editor.ScrollToLine(line); editor.Focus(); }
    }
    private void FindText_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { Find_Click(sender, e); e.Handled = true; } }
    private void GoLineText_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { GoLine_Click(sender, e); e.Handled = true; } }
    private void Documents_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (e.Source == Documents) UpdateStatus(); }
}
