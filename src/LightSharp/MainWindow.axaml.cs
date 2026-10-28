using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;
using LightSharp.Models;
using LightSharp.Services;

namespace LightSharp;

public sealed partial class MainWindow : Window
{
    private readonly WorkspaceService _workspace = new();
    private readonly DotNetService _dotnet = new();
    private readonly CSharpCompletionService _completionService = new();
    private readonly ObservableCollection<FileNode> _roots = [];
    private readonly ObservableCollection<FileNode> _projects = [];
    private readonly ObservableCollection<EditorDocument> _documents = [];
    private string? _workspacePath;
    private EditorDocument? _activeDocument;
    private bool _loadingDocument;
    private CancellationTokenSource? _commandCts;
    private CancellationTokenSource? _completionRequestCts;
    private CompletionWindow? _completionWindow;
    private readonly DispatcherTimer _diagnosticsTimer;

    public MainWindow()
    {
        InitializeComponent();
        FileTree.ItemsSource = _roots;
        StartupProjectSelector.ItemsSource = _projects;
        DocumentTabs.ItemsSource = _documents;
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCursorStatus();
        Editor.TextArea.TextEntered += Editor_TextEntered;
        Closed += (_, _) =>
        {
            _completionRequestCts?.Cancel();
            _completionWindow?.Hide();
            _completionService.Dispose();
        };
        _diagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _diagnosticsTimer.Tick += (_, _) => UpdateDiagnostics();
        _diagnosticsTimer.Start();
        UpdateDiagnostics();
        UpdateCursorStatus();
    }

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open .NET workspace", AllowMultiple = false });
        var folder = folders.FirstOrDefault();
        if (folder is null) return;
        var path = folder.TryGetLocalPath();
        if (path is null) return;
        _workspacePath = path;
        WorkspaceText.Text = Path.GetFileName(path);
        StatusText.Text = "Loading workspace...";
        _roots.Clear();
        _projects.Clear();
        var nodes = await _workspace.LoadAsync(path);
        var solution = nodes.FirstOrDefault(node =>
            Path.GetExtension(node.Name).Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(node.Name).Equals(".slnx", StringComparison.OrdinalIgnoreCase));
        var root = new FileNode
        {
            Name = solution is null ? Path.GetFileName(path) : $"Solution '{solution.Name}'",
            FullPath = path,
            IsDirectory = true
        };
        foreach (var node in nodes)
        {
            root.Children.Add(node);
            AddProjects(node);
        }
        _roots.Add(root);
        StartupProjectSelector.SelectedItem = _projects.FirstOrDefault();
        StatusText.Text = "Ready";
    }

    private async void FileTree_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FileTree.SelectedItem is not FileNode { IsDirectory: false } node) return;
        await OpenDocumentAsync(node);
    }

    private async Task OpenDocumentAsync(FileNode node)
    {
        try
        {
            var document = _documents.FirstOrDefault(item =>
                string.Equals(item.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase));
            if (document is null)
            {
                document = new EditorDocument(node.FullPath, await File.ReadAllTextAsync(node.FullPath));
                _documents.Add(document);
            }
            ActivateDocument(document);
        }
        catch (Exception ex) { AppendOutput(ex.Message); }
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_activeDocument is null) return;
        try
        {
            await File.WriteAllTextAsync(_activeDocument.FullPath, _activeDocument.Text);
            _activeDocument.MarkSaved();
            StatusText.Text = $"Saved {_activeDocument.Name}";
        }
        catch (Exception ex) { AppendOutput(ex.Message); }
    }

    private void Build_Click(object? s, RoutedEventArgs e) => Execute("build");
    private void Test_Click(object? s, RoutedEventArgs e) => Execute("test");
    private void Start_Click(object? s, RoutedEventArgs e)
    {
        if (StartupProjectSelector.SelectedItem is FileNode project)
            Execute("run", "--project", project.FullPath);
        else
            Execute("run");
    }

    private void Stop_Click(object? s, RoutedEventArgs e)
    {
        _commandCts?.Cancel();
        _dotnet.Stop();
        StopButton.IsEnabled = false;
        StatusText.Text = "Stopped";
    }

    private async void Execute(params string[] arguments)
    {
        if (_workspacePath is null) { AppendOutput("Open a workspace first."); return; }
        _commandCts?.Cancel();
        _commandCts?.Dispose();
        var commandCts = new CancellationTokenSource();
        _commandCts = commandCts;
        Output.Text = string.Empty;
        OutputPanel.IsVisible = true;
        var command = string.Join(" ", arguments);
        StatusText.Text = $"dotnet {command}...";
        StopButton.IsEnabled = true;
        var exitCode = await _dotnet.ExecuteAsync(_workspacePath, arguments, AppendOutput, commandCts.Token);
        if (!ReferenceEquals(_commandCts, commandCts)) return;
        _commandCts.Dispose();
        _commandCts = null;
        StopButton.IsEnabled = false;
        StatusText.Text = $"dotnet {command} exited with code {exitCode}";
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e) => Editor.SelectAll();

    private void FocusOutput_Click(object? sender, RoutedEventArgs e)
    {
        OutputPanel.IsVisible = true;
        Output.Focus();
    }

    private void DocumentTab_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: EditorDocument document }) ActivateDocument(document);
    }

    private void CloseDocument_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: EditorDocument document }) return;
        if (document.IsDirty)
        {
            StatusText.Text = "Save changes before closing this tab.";
            return;
        }

        var wasActive = ReferenceEquals(_activeDocument, document);
        _documents.Remove(document);
        if (wasActive) ActivateDocument(_documents.LastOrDefault());
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (_loadingDocument || _activeDocument is null) return;
        _activeDocument.Text = Editor.Text ?? string.Empty;
    }

    private void Editor_TextEntered(object? sender, TextInputEventArgs e)
    {
        if (_completionWindow is not null || string.IsNullOrEmpty(e.Text) || e.Text.Length != 1) return;
        var character = e.Text[0];
        if (character == '.' || char.IsLetterOrDigit(character) || character == '_')
            _ = ShowCompletionAsync(character, explicitInvocation: false);
    }

    private void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || (e.KeyModifiers & KeyModifiers.Control) == 0) return;
        e.Handled = true;
        _ = ShowCompletionAsync(null, explicitInvocation: true);
    }

    private async Task ShowCompletionAsync(char? triggerCharacter, bool explicitInvocation)
    {
        if (_activeDocument is null || _completionWindow is not null ||
            !Path.GetExtension(_activeDocument.FullPath).Equals(".cs", StringComparison.OrdinalIgnoreCase))
            return;

        _completionRequestCts?.Cancel();
        _completionRequestCts?.Dispose();
        var requestCts = new CancellationTokenSource();
        _completionRequestCts = requestCts;

        var activeDocument = _activeDocument;
        var source = Editor.Text ?? string.Empty;
        var caretOffset = Editor.CaretOffset;
        var startOffset = triggerCharacter == '.' ? caretOffset : GetIdentifierStart(source, caretOffset);
        var project = StartupProjectSelector.SelectedItem as FileNode;
        var semanticItems = Array.Empty<CSharpCompletionCandidate>();

        try
        {
            if (!explicitInvocation && triggerCharacter != '.')
                await Task.Delay(90, requestCts.Token);

            if (project is not null)
            {
                semanticItems = (await _completionService.GetSuggestionsAsync(
                    project.FullPath,
                    activeDocument.FullPath,
                    source,
                    caretOffset,
                    triggerCharacter,
                    requestCts.Token)).ToArray();
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            AppendOutput($"C# completion unavailable: {ex.Message}");
        }

        if (requestCts.IsCancellationRequested) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_activeDocument, activeDocument) || Editor.Text != source ||
                requestCts.IsCancellationRequested || _completionWindow is not null)
                return;

            var completionWindow = new CompletionWindow(Editor.TextArea)
            {
                StartOffset = startOffset,
                EndOffset = caretOffset,
                CloseAutomatically = true,
                CloseWhenCaretAtBeginning = triggerCharacter != '.'
            };

            if (semanticItems.Length > 0)
            {
                foreach (var semanticItem in semanticItems)
                {
                    var item = semanticItem;
                    completionWindow.CompletionList.CompletionData.Add(new EditorCompletionItem(
                        item.DisplayText,
                        item.Description,
                        (textArea, segment) => _ = ApplySemanticCompletionAsync(item, textArea, segment)));
                }
            }
            else
            {
                foreach (var word in GetWordSuggestions(source, GetIdentifierStart(source, caretOffset)))
                {
                    var completionText = word;
                    completionWindow.CompletionList.CompletionData.Add(new EditorCompletionItem(
                        completionText,
                        "Word from open documents",
                        (textArea, segment) =>
                        {
                            textArea.Document.Replace(segment.Offset, segment.Length, completionText);
                            textArea.Caret.Offset = segment.Offset + completionText.Length;
                        }));
                }
            }

            if (completionWindow.CompletionList.CompletionData.Count == 0) return;
            _completionWindow = completionWindow;
            completionWindow.Closed += (_, _) =>
            {
                if (ReferenceEquals(_completionWindow, completionWindow)) _completionWindow = null;
            };
            completionWindow.Show();
        });
    }

    private async Task ApplySemanticCompletionAsync(
        CSharpCompletionCandidate candidate,
        AvaloniaEdit.Editing.TextArea textArea,
        AvaloniaEdit.Document.ISegment segment)
    {
        try
        {
            var change = await candidate.CompletionService.GetChangeAsync(
                candidate.Document,
                candidate.Item,
                cancellationToken: CancellationToken.None);
            Dispatcher.UIThread.Post(() =>
            {
                if (_activeDocument?.FullPath != candidate.Document.FilePath ||
                    Editor.Text != candidate.SourceSnapshot)
                    return;

                foreach (var textChange in change.TextChanges.OrderByDescending(item => item.Span.Start))
                    textArea.Document.Replace(textChange.Span.Start, textChange.Span.Length, textChange.NewText ?? string.Empty);

                textArea.Caret.Offset = change.NewPosition ?? (segment.Offset + candidate.DisplayText.Length);
            });
        }
        catch (Exception ex)
        {
            AppendOutput($"Unable to apply completion: {ex.Message}");
        }
    }

    private IEnumerable<string> GetWordSuggestions(string source, int prefixStart)
    {
        var prefix = source[prefixStart..Math.Clamp(Editor.CaretOffset, prefixStart, source.Length)];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var documentText in _documents.Select(document => document.Text).Append(source))
        {
            var position = 0;
            while (position < documentText.Length)
            {
                if (!IsIdentifierStart(documentText[position]))
                {
                    position++;
                    continue;
                }

                var start = position++;
                while (position < documentText.Length && IsIdentifierPart(documentText[position])) position++;
                var word = documentText[start..position];
                if (word.Length > 1 && !word.StartsWith(prefix, StringComparison.Ordinal) && seen.Add(word))
                    yield return word;
            }
        }
    }

    private static int GetIdentifierStart(string source, int position)
    {
        var start = Math.Clamp(position, 0, source.Length);
        while (start > 0 && IsIdentifierPart(source[start - 1])) start--;
        return start;
    }

    private static bool IsIdentifierStart(char character) => character == '_' || char.IsLetter(character);

    private static bool IsIdentifierPart(char character) => character == '_' || char.IsLetterOrDigit(character);

    private void Search_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(SearchBox.Text)) return;
        var query = SearchBox.Text.Trim();
        var match = Flatten(_roots).FirstOrDefault(node => !node.IsDirectory &&
            node.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            StatusText.Text = $"No file matching '{query}'";
            return;
        }

        _ = OpenDocumentAsync(match);
        e.Handled = true;
    }

    private void AppendOutput(string line) => Dispatcher.UIThread.Post(() =>
    {
        Output.Text = (Output.Text ?? string.Empty) + line + Environment.NewLine;
        Output.CaretIndex = Output.Text.Length;
    });

    private void UpdateDiagnostics()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        DiagnosticsText.Text = $"RAM {process.WorkingSet64 / 1024 / 1024:N0} MB";
    }

    private void UpdateCursorStatus()
    {
        var caret = Editor.TextArea.Caret;
        CursorStatusText.Text = $"Ln {caret.Line}, Col {caret.Column}";
    }

    private void ActivateDocument(EditorDocument? document)
    {
        _completionRequestCts?.Cancel();
        _completionWindow?.Hide();
        _completionWindow = null;
        foreach (var item in _documents) item.IsActive = ReferenceEquals(item, document);
        _activeDocument = document;
        _loadingDocument = true;
        Editor.SyntaxHighlighting = EditorHighlightingService.ForFile(document?.FullPath);
        Editor.Text = document?.Text ?? string.Empty;
        _loadingDocument = false;
        EmptyEditorText.IsVisible = document is null;
        UpdateCursorStatus();
        if (document is not null) Editor.Focus();
    }

    private void AddProjects(FileNode node)
    {
        if (!node.IsDirectory && Path.GetExtension(node.Name).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            _projects.Add(node);
        foreach (var child in node.Children) AddProjects(child);
    }

    private static IEnumerable<FileNode> Flatten(IEnumerable<FileNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }
}
