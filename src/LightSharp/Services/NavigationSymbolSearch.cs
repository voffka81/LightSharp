using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LightSharp.Services;

/// <summary>Searches C# declarations without requiring a loaded Roslyn workspace.</summary>
public static class NavigationSymbolSearch
{
    public sealed record Match(string Name, string FilePath, int Line, int Column, string Kind);

    public static IReadOnlyList<Match> Find(
        IEnumerable<(string Path, string Source)> documents,
        string? query, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0) return [];
        var term = (query ?? string.Empty).Trim();
        if (term.Length == 0) return [];
        var matches = new List<Match>();
        foreach (var (path, source) in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path) || source is null) continue;
            var root = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken).GetRoot(cancellationToken);
            foreach (var node in root.DescendantNodes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (name, kind, location) = node switch
                {
                    BaseTypeDeclarationSyntax type => (type.Identifier.ValueText, "Type", type.Identifier.GetLocation()),
                    DelegateDeclarationSyntax del => (del.Identifier.ValueText, "Delegate", del.Identifier.GetLocation()),
                    MethodDeclarationSyntax method => (method.Identifier.ValueText, "Method", method.Identifier.GetLocation()),
                    PropertyDeclarationSyntax property => (property.Identifier.ValueText, "Property", property.Identifier.GetLocation()),
                    EventDeclarationSyntax ev => (ev.Identifier.ValueText, "Event", ev.Identifier.GetLocation()),
                    _ => (null, null, null)
                };
                if (name is null || !name.Contains(term, StringComparison.OrdinalIgnoreCase)) continue;
                var position = location!.GetLineSpan().StartLinePosition;
                matches.Add(new Match(name, path, position.Line + 1, position.Character + 1, kind!));
            }
        }
        return matches
            .OrderBy(m => m.Name.Equals(term, StringComparison.OrdinalIgnoreCase) ? 0 :
                m.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Line)
            .Take(limit).ToArray();
    }
}
