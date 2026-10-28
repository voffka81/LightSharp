using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using System.Xml.Linq;

namespace LightSharp.Services;

public sealed class CSharpCompletionService : IDisposable
{
    private static readonly object RegistrationLock = new();
    private static bool _msBuildRegistered;

    private readonly SemaphoreSlim _workspaceLock = new(1, 1);
    private MSBuildWorkspace? _workspace;
    private string? _projectPath;
    private IReadOnlyDictionary<string, Document> _documents =
        new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<CSharpCompletionCandidate>> GetSuggestionsAsync(
        string projectPath,
        string filePath,
        string source,
        int position,
        char? triggerCharacter,
        CancellationToken cancellationToken)
    {
        var document = await GetDocumentAsync(projectPath, filePath, cancellationToken);
        if (document is null) return [];

        var currentDocument = document.WithText(SourceText.From(source));
        var completionService = CompletionService.GetService(currentDocument);
        if (completionService is null) return [];

        var trigger = triggerCharacter is char character
            ? CompletionTrigger.CreateInsertionTrigger(character)
            : CompletionTrigger.Invoke;
        var list = await completionService.GetCompletionsAsync(
            currentDocument,
            position,
            trigger,
            cancellationToken: cancellationToken);
        if (list is null) return [];

        return list.ItemsList
            .Take(120)
            .Select(item => new CSharpCompletionCandidate(
                item.DisplayText,
                item.FilterText,
                item.InlineDescription,
                currentDocument,
                completionService,
                item,
                source))
            .ToArray();
    }

    public async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(
        string projectPath,
        string filePath,
        string source,
        CancellationToken cancellationToken)
    {
        var document = await GetDocumentAsync(projectPath, filePath, cancellationToken);
        if (document is null) return [];

        var currentDocument = document.WithText(SourceText.From(source));
        var semanticModel = await currentDocument.GetSemanticModelAsync(cancellationToken);
        return semanticModel is null
            ? []
            : semanticModel.GetDiagnostics(cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<(int Start, int Length, string Color)>> GetSemanticHighlightsAsync(
        string projectPath,
        string filePath,
        string source,
        CancellationToken cancellationToken)
    {
        var document = await GetDocumentAsync(projectPath, filePath, cancellationToken);
        if (document is null) return [];
        var currentDocument = document.WithText(SourceText.From(source));
        var model = await currentDocument.GetSemanticModelAsync(cancellationToken);
        var root = await currentDocument.GetSyntaxRootAsync(cancellationToken);
        if (model is null || root is null) return [];

        var spans = new Dictionary<(int Start, int Length), string>();
        void Add(Microsoft.CodeAnalysis.Text.TextSpan span, ISymbol? symbol)
        {
            var color = symbol switch
            {
                INamespaceSymbol => EditorThemeConfiguration.Current.SemanticColor("Namespace"),
                INamedTypeSymbol or ITypeParameterSymbol => EditorThemeConfiguration.Current.SemanticColor("Type"),
                IMethodSymbol => EditorThemeConfiguration.Current.SemanticColor("Method"),
                IPropertySymbol or IEventSymbol => EditorThemeConfiguration.Current.SemanticColor("Property"),
                _ => null
            };
            if (color is not null && span.Length > 0)
                spans[(span.Start, span.Length)] = color;
        }

        foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            var symbolInfo = model.GetSymbolInfo(name, cancellationToken);
            var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
            Add(name.Identifier.Span, symbol);
        }

        foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            Add(declaration.Identifier.Span, model.GetDeclaredSymbol(declaration, cancellationToken));
        foreach (var declaration in root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
            Add(declaration.Identifier.Span, model.GetDeclaredSymbol(declaration, cancellationToken));
        foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            Add(declaration.Identifier.Span, model.GetDeclaredSymbol(declaration, cancellationToken));
        foreach (var declaration in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            Add(declaration.Identifier.Span, model.GetDeclaredSymbol(declaration, cancellationToken));

        return spans.Select(item => (item.Key.Start, item.Key.Length, item.Value)).ToArray();
    }

    public async Task<string> FormatAsync(
        string? projectPath,
        string filePath,
        string source,
        Microsoft.CodeAnalysis.Text.TextSpan? span,
        CancellationToken cancellationToken)
    {
        Document? document = null;
        if (projectPath is not null)
            document = await GetDocumentAsync(projectPath, filePath, cancellationToken);

        using var standaloneWorkspace = document is null ? new AdhocWorkspace() : null;
        if (document is null)
        {
            var project = standaloneWorkspace!.AddProject(Path.GetFileNameWithoutExtension(filePath), LanguageNames.CSharp);
            document = standaloneWorkspace.AddDocument(project.Id, Path.GetFileName(filePath), SourceText.From(source));
        }
        else
        {
            document = document.WithText(SourceText.From(source));
        }

        var formattedDocument = span is Microsoft.CodeAnalysis.Text.TextSpan selection
            ? await Formatter.FormatAsync(document, selection, cancellationToken: cancellationToken)
            : await Formatter.FormatAsync(document, cancellationToken: cancellationToken);
        return (await formattedDocument.GetTextAsync(cancellationToken)).ToString();
    }

    public async Task<string?> GetQuickInfoAsync(
        string projectPath,
        string filePath,
        string source,
        int offset,
        CancellationToken cancellationToken)
    {
        var document = await GetDocumentAsync(projectPath, filePath, cancellationToken);
        if (document is null) return null;
        var currentDocument = document.WithText(SourceText.From(source));
        var model = await currentDocument.GetSemanticModelAsync(cancellationToken);
        var root = await currentDocument.GetSyntaxRootAsync(cancellationToken);
        if (model is null || root is null || source.Length == 0) return null;

        var token = root.FindToken(Math.Clamp(offset, 0, source.Length - 1));
        var node = token.Parent;
        if (node is null) return null;
        var symbolInfo = model.GetSymbolInfo(node, cancellationToken);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()
                     ?? model.GetMemberGroup(node, cancellationToken).FirstOrDefault()
                     ?? model.GetDeclaredSymbol(node, cancellationToken);
        if (symbol is null) return null;

        var signature = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        if (symbol.ContainingType is not null)
            signature = $"{symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}.{signature}";

        var xml = symbol.GetDocumentationCommentXml(cancellationToken: cancellationToken);
        var summary = string.Empty;
        if (!string.IsNullOrWhiteSpace(xml))
        {
            try
            {
                summary = XDocument.Parse(xml).Descendants("summary").FirstOrDefault()?.Value;
                if (!string.IsNullOrWhiteSpace(summary))
                    summary = string.Join(" ", summary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            }
            catch (System.Xml.XmlException) { }
        }
        return string.IsNullOrWhiteSpace(summary) ? signature : $"{signature}{Environment.NewLine}{summary}";
    }

    public async Task<string?> GetExpectedTypeNameAsync(
        string projectPath,
        string filePath,
        string source,
        int offset,
        CancellationToken cancellationToken)
    {
        var document = await GetDocumentAsync(projectPath, filePath, cancellationToken);
        if (document is null || source.Length == 0) return null;
        var currentDocument = document.WithText(SourceText.From(source));
        var model = await currentDocument.GetSemanticModelAsync(cancellationToken);
        var root = await currentDocument.GetSyntaxRootAsync(cancellationToken);
        if (model is null || root is null) return null;

        var probe = Math.Clamp(offset > 0 ? offset - 1 : offset, 0, source.Length - 1);
        var assignment = root.FindToken(probe).Parent?.AncestorsAndSelf()
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault();
        return assignment is null ? null : model.GetTypeInfo(assignment.Left, cancellationToken).Type?.Name;
    }

    public async Task<IReadOnlyList<string>> GetMissingUsingSuggestionsAsync(
        string projectPath,
        string filePath,
        string source,
        int offset,
        CancellationToken cancellationToken)
    {
        var document = await GetDocumentAsync(projectPath, filePath, cancellationToken);
        if (document is null) return [];
        var currentDocument = document.WithText(SourceText.From(source));
        var model = await currentDocument.GetSemanticModelAsync(cancellationToken);
        var root = await currentDocument.GetSyntaxRootAsync(cancellationToken) as CompilationUnitSyntax;
        if (model is null || root is null || source.Length == 0) return [];

        var token = root.FindToken(Math.Clamp(offset, 0, source.Length - 1));
        if (token.Parent is null || !token.IsKind(SyntaxKind.IdentifierToken) ||
            model.GetSymbolInfo(token.Parent!, cancellationToken).Symbol is not null)
            return [];

        var existingUsings = root.Usings
            .Select(directive => directive.Name?.ToString())
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);
        return model.Compilation.GetSymbolsWithName(token.ValueText, SymbolFilter.Type, cancellationToken)
            .OfType<INamedTypeSymbol>()
            .Select(type => type.ContainingNamespace)
            .Where(@namespace => !@namespace.IsGlobalNamespace)
            .Select(@namespace => @namespace.ToDisplayString())
            .Where(name => !existingUsings.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<string?> AddUsingAsync(
        string projectPath,
        string filePath,
        string source,
        string namespaceName,
        CancellationToken cancellationToken)
    {
        var document = await GetDocumentAsync(projectPath, filePath, cancellationToken);
        if (document is null) return null;
        var currentDocument = document.WithText(SourceText.From(source));
        var root = await currentDocument.GetSyntaxRootAsync(cancellationToken) as CompilationUnitSyntax;
        if (root is null || root.Usings.Any(directive => directive.Name?.ToString() == namespaceName)) return null;

        var newRoot = root.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName)));
        var formattedDocument = await Formatter.FormatAsync(
            currentDocument.WithSyntaxRoot(newRoot), cancellationToken: cancellationToken);
        return (await formattedDocument.GetTextAsync(cancellationToken)).ToString();
    }

    public void Dispose()
    {
        _workspace?.Dispose();
        _workspaceLock.Dispose();
    }

    private async Task<Document?> GetDocumentAsync(
        string projectPath,
        string filePath,
        CancellationToken cancellationToken)
    {
        var normalizedProjectPath = Path.GetFullPath(projectPath);
        var normalizedFilePath = Path.GetFullPath(filePath);

        await _workspaceLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.Equals(_projectPath, normalizedProjectPath, StringComparison.OrdinalIgnoreCase))
            {
                _workspace?.Dispose();
                _workspace = null;
                _documents = new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase);
                _projectPath = normalizedProjectPath;

                RegisterMsBuild();
                _workspace = MSBuildWorkspace.Create();
                _workspace.RegisterWorkspaceFailedHandler(diagnostic =>
                    System.Diagnostics.Trace.WriteLine(diagnostic.Diagnostic.Message));
                try
                {
                    // Keep the expensive one-time workspace load alive while the user continues typing.
                    // Canceling this MSBuild host connection for every new prefix can leave the host pipe broken.
                    var project = await _workspace.OpenProjectAsync(
                        normalizedProjectPath,
                        cancellationToken: CancellationToken.None);
                    _documents = project.Documents
                        .Where(document => !string.IsNullOrWhiteSpace(document.FilePath))
                        .GroupBy(document => Path.GetFullPath(document.FilePath!), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                }
                catch
                {
                    _workspace.Dispose();
                    _workspace = null;
                    _projectPath = null;
                    _documents = new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase);
                    throw;
                }
            }

            return _documents.GetValueOrDefault(normalizedFilePath);
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    private static void RegisterMsBuild()
    {
        if (_msBuildRegistered) return;
        lock (RegistrationLock)
        {
            if (_msBuildRegistered) return;
            if (!MSBuildLocator.IsRegistered) MSBuildLocator.RegisterDefaults();
            _msBuildRegistered = true;
        }
    }
}

public sealed record CSharpCompletionCandidate(
    string DisplayText,
    string FilterText,
    string? Description,
    Document Document,
    CompletionService CompletionService,
    CompletionItem Item,
    string SourceSnapshot);
