using System.Collections.ObjectModel;
using Avalonia.Controls;
using LightSharp.Models;
using LightSharp.Views;

namespace LightSharp.Services;

public sealed class SearchReplaceController
{
    private readonly Window _owner;
    private readonly EditorWorkspaceController _editor;
    private readonly EditorWorkspaceView _view;
    private readonly ObservableCollection<FileNode> _roots;
    private readonly ObservableCollection<FileNode> _projects;
    private readonly Func<string?> _solutionPath;
    private readonly Action _refreshGit;
    private readonly OutputPanelView _output;
    private SearchReplaceWindow? _window;
    private Dictionary<string, SearchUndoEntry>? _lastUndo;

    public SearchReplaceController(
        Window owner,
        EditorWorkspaceController editor,
        EditorWorkspaceView view,
        ObservableCollection<FileNode> roots,
        ObservableCollection<FileNode> projects,
        Func<string?> solutionPath,
        Action refreshGit,
        OutputPanelView output)
    {
        _owner = owner;
        _editor = editor;
        _view = view;
        _roots = roots;
        _projects = projects;
        _solutionPath = solutionPath;
        _refreshGit = refreshGit;
        _output = output;
    }

    public void ShowSearchWindow(bool searchEntireSolution, bool replaceMode = false)
    {
        if (_window is { IsVisible: true })
        {
            _window.Activate();
            return;
        }

        var initialQuery = _view.SelectionLength > 0 ? _view.SelectedText : string.Empty;
        var window = new SearchReplaceWindow(
            initialQuery,
            searchEntireSolution,
            SearchAsync,
            FindNextAsync,
            ApplySearchHitsAsync,
            UndoSearchHitsAsync,
            OpenSearchHitAsync,
            replaceMode,
            _lastUndo is { Count: > 0 });
        _window = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window)) _window = null;
        };
        window.Show(_owner);
    }

    public async Task OpenFileMatchingAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var query = text.Trim();
        var match = Flatten(_roots).FirstOrDefault(node => node.IsFile &&
            node.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            _output.StatusMessage = $"No file matching '{query}'";
            return;
        }
        await _editor.OpenDocumentAsync(match);
    }

    private Task<IReadOnlyList<SearchHit>> SearchAsync(SearchRequest request, bool entireSolution)
    {
        var active = _editor.ActiveDocument;
        if (!entireSolution && active is null)
            return Task.FromResult<IReadOnlyList<SearchHit>>([]);

        var activePath = active?.FullPath;
        IEnumerable<string> paths = entireSolution
            ? Flatten(_roots)
                .Where(node => node.IsFile && IsSearchableTextFile(node.FullPath))
                .Select(node => node.FullPath)
                .Concat(_projects.Select(project => project.FullPath))
                .Concat(_solutionPath() is { } solution ? [solution] : [])
                .Concat(activePath is null ? [] : [activePath])
                .Distinct(StringComparer.OrdinalIgnoreCase)
            : [activePath!];
        var openTexts = _editor.Documents.ToDictionary(document => document.FullPath, document => document.Text,
            StringComparer.OrdinalIgnoreCase);
        return SearchReplaceService.SearchAsync(paths, openTexts, request);
    }

    private Task<SearchHit?> FindNextAsync(SearchRequest request)
    {
        var document = _editor.ActiveDocument;
        if (document is null) return Task.FromResult<SearchHit?>(null);
        var hits = SearchReplaceService.FindInText(document.FullPath, document.Text, request);
        if (hits.Count == 0) return Task.FromResult<SearchHit?>(null);
        var start = _view.SelectionLength > 0
            ? _view.SelectionStart + _view.SelectionLength
            : _view.CaretOffset;
        return Task.FromResult<SearchHit?>(hits.FirstOrDefault(hit => hit.Start >= start) ?? hits[0]);
    }

    private static bool IsSearchableTextFile(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".cs" or ".csx" or ".xaml" or ".axaml" or ".xml" or ".json" or ".config" or ".csproj" or
        ".props" or ".targets" or ".sln" or ".slnx" or ".md" or ".txt" or ".editorconfig" or
        ".yml" or ".yaml" or ".razor" or ".cshtml";

    private async Task<int> ApplySearchHitsAsync(IReadOnlyList<SearchHit> hits)
    {
        var undoEntries = new Dictionary<string, SearchUndoEntry>(StringComparer.OrdinalIgnoreCase);
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in hits.GroupBy(hit => hit.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            var document = FindDocument(group.Key);
            var original = document?.Text ?? await File.ReadAllTextAsync(group.Key);
            var updated = original;
            var applicable = group
                .Where(hit => hit.Start >= 0 && hit.Start + hit.Length <= original.Length &&
                              string.Equals(original.Substring(hit.Start, hit.Length), hit.MatchedText, StringComparison.Ordinal))
                .OrderByDescending(hit => hit.Start)
                .ToArray();
            if (applicable.Length == 0) continue;
            foreach (var hit in applicable)
                updated = updated[..hit.Start] + hit.ReplacementText + updated[(hit.Start + hit.Length)..];
            undoEntries[group.Key] = new SearchUndoEntry(original, updated);
            replacements[group.Key] = updated;
            counts[group.Key] = applicable.Length;
        }

        if (replacements.Count == 0) return 0;
        var changedDiskFiles = new List<string>();
        try
        {
            foreach (var path in replacements.Keys.ToArray())
            {
                if (FindDocument(path) is not null) continue;
                var current = await File.ReadAllTextAsync(path);
                if (!string.Equals(current, undoEntries[path].Before, StringComparison.Ordinal))
                {
                    RemoveReplacement(path, replacements, undoEntries, counts);
                    continue;
                }
                await File.WriteAllTextAsync(path, replacements[path]);
                changedDiskFiles.Add(path);
            }
        }
        catch
        {
            foreach (var path in changedDiskFiles)
                try { await File.WriteAllTextAsync(path, undoEntries[path].Before); } catch { }
            throw;
        }

        foreach (var pair in replacements.ToArray())
        {
            var document = FindDocument(pair.Key);
            if (document is null || string.Equals(document.Text, undoEntries[pair.Key].After, StringComparison.Ordinal)) continue;
            if (!string.Equals(document.Text, undoEntries[pair.Key].Before, StringComparison.Ordinal))
            {
                RemoveReplacement(pair.Key, replacements, undoEntries, counts);
                continue;
            }
            if (ReferenceEquals(document, _editor.ActiveDocument) && _view.HasDocument)
                _view.ReplaceAllText(pair.Value);
            else
                document.Text = pair.Value;
        }

        _lastUndo = undoEntries;
        _refreshGit();
        return counts.Values.Sum();
    }

    private async Task<int> UndoSearchHitsAsync()
    {
        if (_lastUndo is not { Count: > 0 }) return 0;
        var restored = 0;
        foreach (var pair in _lastUndo)
        {
            var document = FindDocument(pair.Key);
            var current = document?.Text ?? await File.ReadAllTextAsync(pair.Key);
            if (!string.Equals(current, pair.Value.After, StringComparison.Ordinal)) continue;
            if (document is null)
                await File.WriteAllTextAsync(pair.Key, pair.Value.Before);
            else if (ReferenceEquals(document, _editor.ActiveDocument) && _view.HasDocument)
                _view.ReplaceAllText(pair.Value.Before);
            else
                document.Text = pair.Value.Before;
            restored++;
        }
        _lastUndo = null;
        if (restored > 0) _refreshGit();
        return restored;
    }

    private async Task OpenSearchHitAsync(SearchHit hit)
    {
        var document = FindDocument(hit.FilePath);
        if (document is null)
        {
            await _editor.OpenDocumentAsync(new FileNode { Name = Path.GetFileName(hit.FilePath), FullPath = hit.FilePath });
            document = FindDocument(hit.FilePath);
        }
        if (document is null) return;
        _editor.SetActiveDocument(document);
        if (!_view.HasDocument) return;
        var start = Math.Clamp(hit.Start, 0, _view.TextLength);
        var length = Math.Clamp(hit.Length, 0, _view.TextLength - start);
        _view.Select(start, length);
        _view.ScrollToLine(Math.Clamp(hit.Line, 1, _view.LineCount));
        _view.FocusEditor();
    }

    private EditorDocument? FindDocument(string path) => _editor.Documents.FirstOrDefault(document =>
        string.Equals(document.FullPath, path, StringComparison.OrdinalIgnoreCase));

    private static void RemoveReplacement(
        string path,
        IDictionary<string, string> replacements,
        IDictionary<string, SearchUndoEntry> undoEntries,
        IDictionary<string, int> counts)
    {
        replacements.Remove(path);
        undoEntries.Remove(path);
        counts.Remove(path);
    }

    private static IEnumerable<FileNode> Flatten(IEnumerable<FileNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    private sealed record SearchUndoEntry(string Before, string After);
}
