using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

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
                item.InlineDescription,
                currentDocument,
                completionService,
                item,
                source))
            .ToArray();
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
                var project = await _workspace.OpenProjectAsync(normalizedProjectPath, cancellationToken: cancellationToken);
                _documents = project.Documents
                    .Where(document => !string.IsNullOrWhiteSpace(document.FilePath))
                    .GroupBy(document => Path.GetFullPath(document.FilePath!), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
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
    string? Description,
    Document Document,
    CompletionService CompletionService,
    CompletionItem Item,
    string SourceSnapshot);
