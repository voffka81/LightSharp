using System.Text.Json;
using LightSharp.Models;

namespace LightSharp.Services;

public sealed record RecoverySnapshot(string Path, string SavedText, string DraftText);

/// <summary>Stores dirty buffers outside source files for recovery after an unexpected exit.</summary>
public sealed class EditorRecoveryStore
{
    private readonly string _path;

    public EditorRecoveryStore(string? path = null) =>
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LightSharp", "recovery.json");

    public void Save(IEnumerable<EditorDocument> documents)
    {
        var snapshots = documents.Where(d => d.IsDirty)
            .Select(d => new RecoverySnapshot(d.FullPath, d.SavedText, d.Text)).ToArray();
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path))!;
        Directory.CreateDirectory(directory);
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshots));
            File.Move(temp, _path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public RecoverySnapshot[] Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            return JsonSerializer.Deserialize<RecoverySnapshot[]>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
