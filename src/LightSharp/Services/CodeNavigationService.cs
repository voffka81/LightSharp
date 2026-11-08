using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace LightSharp.Services;

public sealed record CodeDefinitionLocation(string FilePath, int Line, int Column);

/// <summary>Resolves source definitions without depending on Avalonia controls.</summary>
public static class CodeNavigationService
{
    public static async Task<CodeDefinitionLocation?> FindDefinitionAsync(
        Document document, int offset, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        var model = await document.GetSemanticModelAsync(cancellationToken);
        if (root is null || model is null || root.FullSpan.IsEmpty) return null;

        var position = Math.Clamp(offset, 0, root.FullSpan.End - 1);
        var token = root.FindToken(position);
        if (!token.IsKind(SyntaxKind.IdentifierToken) && position > 0)
            token = root.FindToken(position - 1);
        if (!token.IsKind(SyntaxKind.IdentifierToken)) return null;

        var node = token.Parent;
        if (node is null) return null;
        var symbol = model.GetSymbolInfo(node, cancellationToken).Symbol
            ?? model.GetDeclaredSymbol(node, cancellationToken)
            ?? node.Ancestors().Select(parent => model.GetDeclaredSymbol(parent, cancellationToken))
                .FirstOrDefault(candidate => candidate is not null);
        if (symbol is null) return null;

        var location = symbol.Locations.FirstOrDefault(item =>
            item.IsInSource && !string.IsNullOrEmpty(item.SourceTree?.FilePath));
        if (location is null) return null;

        var span = location.GetLineSpan();
        return new CodeDefinitionLocation(
            span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
    }
}
