using System.Xml.Linq;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace LightSharp.Services;

public static class EditorHighlightingService
{
    private static readonly Lazy<IHighlightingDefinition> CSharpHighlighting =
        new(() => CreateThemedHighlighting(".CSharp-Mode.xshd", EditorThemeConfiguration.Current.CSharpColors, true));
    private static readonly Lazy<IHighlightingDefinition> XmlHighlighting =
        new(() => CreateThemedHighlighting(".XML-Mode.xshd", EditorThemeConfiguration.Current.XmlColors));
    private static readonly Lazy<IHighlightingDefinition> JsonHighlighting =
        new(() => CreateThemedHighlighting(".Json.xshd", EditorThemeConfiguration.Current.JsonColors));

    public static IHighlightingDefinition? ForFile(string? path) => Path.GetExtension(path ?? string.Empty).ToLowerInvariant() switch
    {
        ".cs" => CSharpHighlighting.Value,
        ".xml" or ".xaml" or ".axaml" or ".config" or ".csproj" or ".props" or ".targets" or ".slnx" => XmlHighlighting.Value,
        ".json" => JsonHighlighting.Value,
        _ => null
    };

    private static IHighlightingDefinition CreateThemedHighlighting(
        string resourceSuffix,
        IReadOnlyDictionary<string, string> colors,
        bool configureCSharp = false)
    {
        var assembly = typeof(HighlightingManager).Assembly;
        var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(name =>
            name.EndsWith(resourceSuffix, StringComparison.Ordinal));
        if (resourceName is null)
            throw new InvalidOperationException($"AvaloniaEdit's highlighting resource '{resourceSuffix}' was not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"AvaloniaEdit's highlighting resource '{resourceSuffix}' could not be opened.");
        var document = XDocument.Load(stream);
        foreach (var element in document.Descendants().Where(element => element.Name.LocalName == "Color"))
        {
            var name = (string?)element.Attribute("name");
            if (name is not null && colors.TryGetValue(name, out var color))
                element.SetAttributeValue("foreground", color);
        }

        if (configureCSharp) ConfigureCSharpRules(document, EditorThemeConfiguration.Current);
        using var reader = document.CreateReader();
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private static void ConfigureCSharpRules(XDocument document, EditorThemeConfiguration configuration)
    {
        var root = document.Root ?? throw new InvalidOperationException("C# highlighting definition is empty.");
        var ns = root.Name.Namespace;
        var ruleSet = root.Elements(ns + "RuleSet").FirstOrDefault()
            ?? throw new InvalidOperationException("C# highlighting rules were not found.");

        var methodRule = ruleSet.Elements(ns + "Rule").FirstOrDefault(rule =>
            (string?)rule.Attribute("color") == "MethodCall");
        methodRule?.AddBeforeSelf(new XElement(ns + "Rule",
            new XAttribute("color", "Attribute"), configuration.AttributePattern));

        var punctuationRule = ruleSet.Elements(ns + "Rule").FirstOrDefault(rule =>
            (string?)rule.Attribute("color") == "Punctuation");
        punctuationRule?.AddBeforeSelf(new XElement(ns + "Rule",
            new XAttribute("color", "Operator"), configuration.OperatorPattern));

        var keywordList = ruleSet.Elements(ns + "Keywords").FirstOrDefault(element =>
            (string?)element.Attribute("color") == "Keywords");
        if (keywordList is null) return;
        var existing = keywordList.Elements(ns + "Word").Select(word => word.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var keyword in configuration.AdditionalKeywords)
            if (existing.Add(keyword)) keywordList.Add(new XElement(ns + "Word", keyword));
    }
}