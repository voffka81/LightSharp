using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LightSharp.Views;
using LightSharp.Models;

namespace LightSharp.Services;

/// <summary>Coordinates editor session and dirty-buffer recovery outside the application shell.</summary>
public sealed class EditorRecoveryController : IDisposable
{
    private readonly EditorWorkspaceController _editor;
    private readonly Action<string> _log;
    private readonly Window _owner;
    private readonly EditorSessionStore _sessionStore = new();
    private readonly EditorRecoveryStore _recoveryStore = new();
    private readonly Dictionary<string, RecoverySnapshot> _unresolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };

    public EditorRecoveryController(EditorWorkspaceController editor, Action<string> log, Window owner)
    {
        _editor = editor;
        _log = log;
        _owner = owner;
        _timer.Tick += OnTick;
    }

    public async Task StartAsync()
    {
        await RestoreEditorSessionAsync();
        await RestoreRecoverySnapshotsAsync();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        SaveEditorSession();
        SaveRecoverySnapshot();
    }

    private void OnTick(object? sender, EventArgs e) => SaveRecoverySnapshot();

    public void Dispose() => _timer.Stop();

    private async Task RestoreEditorSessionAsync()
    {
        var session = _sessionStore.Load();
        foreach (var path in session.OpenFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            await _editor.OpenDocumentAsync(new FileNode
            {
                Name = Path.GetFileName(path),
                FullPath = path,
                IsDirectory = false
            });
        }

        if (session.ActiveFile is { } active)
        {
            var document = _editor.Documents.FirstOrDefault(
                item => string.Equals(item.FullPath, active, StringComparison.OrdinalIgnoreCase));
            if (document is not null) _editor.SetActiveDocument(document);
        }
    }

    private void SaveEditorSession()
    {
        try
        {
            _sessionStore.Save(new EditorSession(
                _editor.Documents.Select(item => item.FullPath).ToArray(),
                _editor.ActiveDocument?.FullPath));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cannot save editor session: {ex.Message}");
        }
    }


    private async Task RestoreRecoverySnapshotsAsync()
    {
        foreach (var snapshot in _recoveryStore.Load())
        {
            if (string.IsNullOrWhiteSpace(snapshot.Path)) continue;
            if (!File.Exists(snapshot.Path))
            {
                // The original path is missing: never silently recreate it.
                _unresolved[snapshot.Path] = snapshot;
                try
                {
                    var choice = await new MissingFileRecoveryDialog(Path.GetFileName(snapshot.Path))
                        .ShowDialog<MissingFileRecoveryChoice>(_owner);
                    if (choice == MissingFileRecoveryChoice.DiscardBackup)
                    {
                        _unresolved.Remove(snapshot.Path);
                        _log($"Missing-file recovery discarded by user: {snapshot.Path}");
                    }
                    else if (choice == MissingFileRecoveryChoice.ExportDraft)
                    {
                        var selected = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                        {
                            Title = "Export recovered draft as a new file",
                            SuggestedFileName = Path.GetFileName(snapshot.Path) + ".recovered"
                        });
                        if (selected is not null && selected.Path.IsFile)
                        {
                            await MissingFileRecoveryExporter.ExportAsync(selected.Path.LocalPath, snapshot);
                            _unresolved.Remove(snapshot.Path);
                            _log($"Recovered draft exported: {selected.Path.LocalPath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log($"Recovery export failed; backup retained for {snapshot.Path}: {ex.Message}");
                }
                continue;
            }
            try
            {
                // Never apply a draft over a file that has changed on disk.
                if (!string.Equals(await File.ReadAllTextAsync(snapshot.Path), snapshot.SavedText, StringComparison.Ordinal))
                {
                    var choice = await new RecoveryConflictDialog(Path.GetFileName(snapshot.Path))
                        .ShowDialog<RecoveryConflictChoice>(_owner);
                    if (choice == RecoveryConflictChoice.KeepBackup)
                    {
                        _unresolved[snapshot.Path] = snapshot;
                        _log($"Recovery backup retained: {snapshot.Path}");
                    }
                    else if (choice == RecoveryConflictChoice.RestoreDraft)
                    {
                        var recoveryDocument = _editor.Documents.FirstOrDefault(item =>
                            string.Equals(item.FullPath, snapshot.Path, StringComparison.OrdinalIgnoreCase));
                        if (recoveryDocument is null)
                        {
                            await _editor.OpenDocumentAsync(new FileNode
                            {
                                Name = Path.GetFileName(snapshot.Path), FullPath = snapshot.Path, IsDirectory = false
                            });
                            recoveryDocument = _editor.ActiveDocument;
                        }
                        if (recoveryDocument is not null && !recoveryDocument.IsDirty)
                        {
                            recoveryDocument.Text = snapshot.DraftText;
                            if (ReferenceEquals(recoveryDocument, _editor.ActiveDocument)) _editor.SetActiveDocument(recoveryDocument);
                            _log($"Recovered unsaved draft: {snapshot.Path}");
                        }
                        else
                        {
                            _unresolved[snapshot.Path] = snapshot;
                            _log($"Recovery retained because editor is already dirty: {snapshot.Path}");
                        }
                    }
                    else _log($"Recovery backup discarded by user: {snapshot.Path}");
                    continue;
                }

                var document = _editor.Documents.FirstOrDefault(item =>
                    string.Equals(item.FullPath, snapshot.Path, StringComparison.OrdinalIgnoreCase));
                if (document is null)
                {
                    await _editor.OpenDocumentAsync(new FileNode
                    {
                        Name = Path.GetFileName(snapshot.Path),
                        FullPath = snapshot.Path,
                        IsDirectory = false
                    });
                    document = _editor.ActiveDocument;
                }
                if (document is not null && !document.IsDirty &&
                    string.Equals(document.FullPath, snapshot.Path, StringComparison.OrdinalIgnoreCase))
                {
                    document.Text = snapshot.DraftText;
                    if (ReferenceEquals(document, _editor.ActiveDocument))
                        _editor.SetActiveDocument(document);
                }
            }
            catch (Exception ex)
            {
                _unresolved[snapshot.Path] = snapshot;
                _log($"Recovery failed for {snapshot.Path}: {ex.Message}");
            }
        }
    }

    private void SaveRecoverySnapshot()
    {
        try { _recoveryStore.Save(_editor.Documents, _unresolved.Values); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cannot save recovery snapshot: {ex.Message}");
        }
    }

}
