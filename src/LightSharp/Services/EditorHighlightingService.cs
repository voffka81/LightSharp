using System.Xml.Linq;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace LightSharp.Services;

public static class EditorHighlightingService
{
    private static readonly IReadOnlyDictionary<string, string> CSharpColors = new Dictionary<string, string>
    {
        ["Comment"] = "#6A9955",
        ["String"] = "#CE9178",
        ["StringInterpolation"] = "#D4D4D4",
        ["Char"] = "#CE9178",
        ["Preprocessor"] = "#C586C0",
        ["ValueTypeKeywords"] = "#569CD6",
        ["ReferenceTypeKeywords"] = "#569CD6",
        ["MethodCall"] = "#DCDCAA",
        ["NumberLiteral"] = "#B5CEA8",
        ["ThisOrBaseReference"] = "#569CD6",
        ["NullOrValueKeywords"] = "#569CD6",
        ["Keywords"] = "#569CD6",
        ["GotoKeywords"] = "#569CD6",
        ["ContextKeywords"] = "#569CD6",
        ["ExceptionKeywords"] = "#569CD6",
        ["CheckedKeyword"] = "#569CD6",
        ["UnsafeKeywords"] = "#569CD6",
        ["OperatorKeywords"] = "#569CD6",
        ["ParameterModifiers"] = "#569CD6",
        ["Modifiers"] = "#569CD6",
        ["Visibility"] = "#569CD6",
        ["NamespaceKeywords"] = "#569CD6",
        ["GetSetAddRemove"] = "#569CD6",
        ["TrueFalse"] = "#569CD6",
        ["TypeKeywords"] = "#569CD6",
        ["SemanticKeywords"] = "#569CD6",
        ["Punctuation"] = "#D4D4D4",
        ["Operator"] = "#D4D4D4",
        ["Attribute"] = "#4EC9B0"
    };

    private static readonly IReadOnlyDictionary<string, string> XmlColors = new Dictionary<string, string>
    {
        ["Comment"] = "#6A9955",
        ["CData"] = "#CE9178",
        ["DocType"] = "#569CD6",
        ["XmlDeclaration"] = "#569CD6",
        ["XmlTag"] = "#569CD6",
        ["AttributeName"] = "#9CDCFE",
        ["AttributeValue"] = "#CE9178",
        ["Entity"] = "#D7BA7D",
        ["BrokenEntity"] = "#F44747"
    };

    private static readonly IReadOnlyDictionary<string, string> JsonColors = new Dictionary<string, string>
    {
        ["Bool"] = "#569CD6",
        ["Number"] = "#B5CEA8",
        ["String"] = "#CE9178",
        ["Null"] = "#569CD6",
        ["FieldName"] = "#9CDCFE",
        ["Punctuation"] = "#D4D4D4"
    };

    private static readonly Lazy<IHighlightingDefinition> CSharpHighlighting =
        new(() => CreateThemedHighlighting(".CSharp-Mode.xshd", CSharpColors));
    private static readonly Lazy<IHighlightingDefinition> XmlHighlighting =
        new(() => CreateThemedHighlighting(".XML-Mode.xshd", XmlColors));
    private static readonly Lazy<IHighlightingDefinition> JsonHighlighting =
        new(() => CreateThemedHighlighting(".Json.xshd", JsonColors));

    public static IHighlightingDefinition? ForFile(string? path)
    {
        return Path.GetExtension(path ?? string.Empty).ToLowerInvariant() switch
        {
            ".cs" => CSharpHighlighting.Value,
            ".xml" or ".xaml" or ".axaml" or ".config" or ".csproj" or ".props" or ".targets" or ".slnx" => XmlHighlighting.Value,
            ".json" => JsonHighlighting.Value,
            _ => null
        };
    }

    private static IHighlightingDefinition CreateThemedHighlighting(
        string resourceSuffix,
        IReadOnlyDictionary<string, string> colors)
    {
        var assembly = typeof(HighlightingManager).Assembly;
        var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(name =>
            name.EndsWith(resourceSuffix, StringComparison.Ordinal));
        if (resourceName is null)
            throw new InvalidOperationException($"AvaloniaEdit's highlighting resource '{resourceSuffix}' was not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"AvaloniaEdit's highlighting resource '{resourceSuffix}' could not be opened.");
        var document = XDocument.Load(stream);
        var colorElements = document.Descendants()
            .Where(element => element.Name.LocalName == "Color");
        foreach (var element in colorElements)
        {
            var name = (string?)element.Attribute("name");
            if (name is not null && colors.TryGetValue(name, out var color))
                element.SetAttributeValue("foreground", color);
        }

        if (resourceSuffix == ".CSharp-Mode.xshd")
            ConfigureCSharpRules(document);

        using var reader = document.CreateReader();
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private static void ConfigureCSharpRules(XDocument document)
    {
        var root = document.Root ?? throw new InvalidOperationException("C# highlighting definition is empty.");
        var ns = root.Name.Namespace;
        var ruleSet = root.Elements(ns + "RuleSet").FirstOrDefault()
            ?? throw new InvalidOperationException("C# highlighting rules were not found.");

        var methodRule = ruleSet.Elements(ns + "Rule").FirstOrDefault(rule =>
            (string?)rule.Attribute("color") == "MethodCall");
        methodRule?.AddBeforeSelf(new XElement(ns + "Rule",
            new XAttribute("color", "Attribute"),
            @"(?<=\[)\s*[\w.]+(?=\s*(?:\(|,|\]))"));

        var punctuationRule = ruleSet.Elements(ns + "Rule").FirstOrDefault(rule =>
            (string?)rule.Attribute("color") == "Punctuation");
        punctuationRule?.AddBeforeSelf(new XElement(ns + "Rule",
            new XAttribute("color", "Operator"),
            @"(?:>>>=|<<=|>>=|=>|==|!=|<=|>=|&&|\|\||\?\?|\+\+|--|\+=|-=|\*=|/=|%=|&=|\|=|\^=|<<|>>|[=+\-*/%&|^!~<>?:])"));

        var keywordList = ruleSet.Elements(ns + "Keywords").FirstOrDefault(element =>
            (string?)element.Attribute("color") == "Keywords");
        if (keywordList is null) return;
        var existing = keywordList.Elements(ns + "Word").Select(word => word.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var keyword in new[] { "record", "init", "required", "file", "scoped", "with", "not", "and", "or" })
            if (existing.Add(keyword)) keywordList.Add(new XElement(ns + "Word", keyword));
    }
}
