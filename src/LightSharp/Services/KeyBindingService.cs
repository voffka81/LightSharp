using System.Text.Json;
using Avalonia.Input;

namespace LightSharp.Services;

/// <summary>Loads defaults and applies optional per-user shortcut overrides.</summary>
public sealed class KeyBindingService
{
    private readonly Dictionary<string, string[]> _defaults;
    private readonly Dictionary<string, string[]> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _userFile;
    public event EventHandler? Changed;
    private readonly Dictionary<string, InputCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _prefixes = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Warnings => _warnings;
    private readonly List<string> _warnings = new();

    public KeyBindingService(string? userFile = null)
    {
        _defaults = LoadDefaults();
        _userFile = userFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LightSharp", "keymap.json");
        var path = _userFile;
        if (File.Exists(path))
        {
            try
            {
                var overrides = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path))
                    ?? throw new JsonException("Empty keymap");
                foreach (var (command, gestures) in overrides)
                {
                    if (!Enum.TryParse<InputCommand>(command, true, out _))
                    {
                        _warnings.Add($"Unknown command: {command}");
                        continue;
                    }
                    if (gestures is null || gestures.Any(g => !TryNormalizeGesture(g, out _)))
                    {
                        _warnings.Add($"Invalid shortcuts for {command}; defaults retained");
                        continue;
                    }
                    _overrides[command] = gestures;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _warnings.Add($"Cannot load user keymap: {ex.Message}");
            }
        }
        Rebuild();
    }

    public IReadOnlyDictionary<string, string[]> GetBindings()
    {
        var result = new Dictionary<string, string[]>(_defaults, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in _overrides) result[key] = value;
        return result;
    }

    public bool TrySetBindings(InputCommand command, string[] gestures, out string? error)
    {
        error = null;
        var normalized = new List<string>();
        foreach (var gesture in gestures)
        {
            if (!TryNormalizeGesture(gesture, out var value))
            {
                error = $"Invalid shortcut: {gesture}";
                return false;
            }
            normalized.Add(value);
        }
        if (normalized.Count != normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            error = "Duplicate shortcut for the same command";
            return false;
        }
        foreach (var (other, keys) in GetBindings())
        {
            if (other.Equals(command.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var key in keys)
            {
                if (!TryNormalizeGesture(key, out var value)) continue;
                if (normalized.Any(n => n.Equals(value, StringComparison.OrdinalIgnoreCase)
                    || n.StartsWith(value + " ", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith(n + " ", StringComparison.OrdinalIgnoreCase)))
                {
                    error = $"Shortcut conflicts with {other}: {value}";
                    return false;
                }
            }
        }
        var name = command.ToString();
        var hadPrevious = _overrides.TryGetValue(name, out var previous);
        if (_defaults.TryGetValue(name, out var defaults) && defaults.SequenceEqual(gestures, StringComparer.OrdinalIgnoreCase))
            _overrides.Remove(name);
        else _overrides[name] = gestures;
        try { SaveOverrides(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (hadPrevious) _overrides[name] = previous!;
            else _overrides.Remove(name);
            error = ex.Message;
            return false;
        }
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void ResetAll()
    {
        var previous = new Dictionary<string, string[]>(_overrides, StringComparer.OrdinalIgnoreCase);
        _overrides.Clear();
        try { SaveOverrides(); }
        catch
        {
            foreach (var (key, value) in previous) _overrides[key] = value;
            throw;
        }
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SaveOverrides()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_userFile))!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(_userFile, JsonSerializer.Serialize(_overrides, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Rebuild()
    {
        _commands.Clear();
        _prefixes.Clear();
        _warnings.Clear();
        foreach (var (name, gestures) in GetBindings())
        {
            if (!Enum.TryParse<InputCommand>(name, true, out var command)) continue;
            foreach (var gesture in gestures)
            {
                if (!TryNormalizeGesture(gesture, out var normalized)) continue;
                if (_commands.TryGetValue(normalized, out var existing))
                {
                    _warnings.Add($"Shortcut conflict: {normalized} ({existing}, {command})");
                    continue;
                }
                _commands.Add(normalized, command);
                var parts = normalized.Split(' ');
                for (var i = 1; i < parts.Length; i++)
                    _prefixes.Add(string.Join(' ', parts.Take(i)));
            }
        }
    }

    private static Dictionary<string, string[]> LoadDefaults()
    {
        using var stream = typeof(KeyBindingService).Assembly.GetManifestResourceStream("LightSharp.Keymaps.default-keymap.json")
            ?? throw new InvalidOperationException("Bundled default keymap is missing");
        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)
            ?? throw new InvalidOperationException("Bundled default keymap is empty");
    }

    public bool IsPrefix(string gesture) => _prefixes.Contains(gesture);
    public InputCommand? Resolve(string gesture) => _commands.TryGetValue(gesture, out var command) ? command : null;

    public static bool IsValidGesture(string gesture) => TryNormalizeGesture(gesture, out _);

    public static string Normalize(Key key, KeyModifiers modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Meta");
        if (modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private static bool TryNormalizeGesture(string? gesture, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        var steps = new List<string>();
        foreach (var step in gesture.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var tokens = step.Split('+', StringSplitOptions.TrimEntries);
            if (tokens.Length == 0 || !Enum.TryParse<Key>(tokens[^1], true, out var key) || key == Key.None) return false;
            var mods = KeyModifiers.None;
            foreach (var token in tokens[..^1])
            {
                switch (token.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= KeyModifiers.Control; break;
                    case "cmd": case "meta": mods |= KeyModifiers.Meta; break;
                    case "alt": mods |= KeyModifiers.Alt; break;
                    case "shift": mods |= KeyModifiers.Shift; break;
                    default: return false;
                }
            }
            steps.Add(Normalize(key, mods));
        }
        normalized = string.Join(' ', steps);
        return true;
    }
}
