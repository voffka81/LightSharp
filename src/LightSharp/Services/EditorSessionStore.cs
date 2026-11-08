using System.Text.Json;

namespace LightSharp.Services;

public sealed record EditorSession(string[] OpenFiles, string? ActiveFile);

/// <summary>Persists only file paths; never writes unsaved editor text.</summary>
public sealed class EditorSessionStore
{
    private readonly string _path;

    public EditorSessionStore(string? path = null) =>
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LightSharp", "editor-session.json");

    public EditorSession Load()
    {
        try
        {
            if (!File.Exists(_path)) return new EditorSession([], null);
            var session = JsonSerializer.Deserialize<EditorSession>(File.ReadAllText(_path));
            return session is { OpenFiles: not null } ? session : new EditorSession([], null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new EditorSession([], null);
        }
    }

    public void Save(EditorSession session)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        Directory.CreateDirectory(directory);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(session));
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
