using System.Reflection;
using System.Text.Json;

namespace LightSharp.Services;

public sealed class EditorThemeConfiguration
{
    private static readonly Lazy<EditorThemeConfiguration> CurrentConfiguration = new(Load);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static EditorThemeConfiguration Current => CurrentConfiguration.Value;

    public Dictionary<string, string> CSharpColors { get; init; } = [];
    public Dictionary<string, string> XmlColors { get; init; } = [];
    public Dictionary<string, string> JsonColors { get; init; } = [];
    public Dictionary<string, string> SemanticColors { get; init; } = [];
    public required string SyntaxErrorColor { get; init; }
    public string AttributePattern { get; init; } = string.Empty;
    public string OperatorPattern { get; init; } = string.Empty;
    public string[] AdditionalKeywords { get; init; } = [];

    public string SemanticColor(string name) => SemanticColors.TryGetValue(name, out var color)
        ? color
        : throw new InvalidOperationException($"Semantic color '{name}' is missing from the editor theme configuration.");

    private static EditorThemeConfiguration Load()
    {
        var assembly = typeof(EditorThemeConfiguration).Assembly;
        const string resourceName = "LightSharp.Styles.EditorHighlighting.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Editor theme configuration '{resourceName}' was not found.");
        return JsonSerializer.Deserialize<EditorThemeConfiguration>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Editor theme configuration is empty.");
    }
}
