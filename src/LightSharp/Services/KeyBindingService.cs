using System.Text.Json;
using Avalonia.Input;

namespace LightSharp.Services;

/// <summary>Loads defaults and applies optional per-user shortcut overrides.</summary>
public sealed class KeyBindingService
{
    private readonly Dictionary<string, InputCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _prefixes = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Warnings => _warnings;
    private readonly List<string> _warnings = new();

    public KeyBindingService(string? userFile = null)
    {
        var defaults = LoadDefaults();
        var path = userFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LightSharp", "keymap.json");
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
                    defaults[command] = gestures;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _warnings.Add($"Cannot load user keymap: {ex.Message}");
            }
        }
        foreach (var (name, gestures) in defaults)
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
