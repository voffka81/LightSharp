using System.Collections.ObjectModel;
using LightSharp.Models;
using LightSharp.Views;

namespace LightSharp.Services;

/// <summary>Owns the loaded workspace tree, solution selection, and startup project state.</summary>
public sealed class WorkspaceController
{
    private readonly WorkspaceService _workspace;
    private readonly OutputPanelView _output;
    private readonly PropertiesView _properties;
    private readonly SolutionExplorerView _solutionExplorer;
    private readonly MainToolbarView _toolbar;
    private readonly GitChangesController _git;
    private readonly Func<string?> _activeDocumentPath;

    public WorkspaceController(
        WorkspaceService workspace,
        OutputPanelView output,
        PropertiesView properties,
        SolutionExplorerView solutionExplorer,
        MainToolbarView toolbar,
        GitChangesController git,
        Func<string?> activeDocumentPath)
    {
        _workspace = workspace;
        _output = output;
        _properties = properties;
        _solutionExplorer = solutionExplorer;
        _toolbar = toolbar;
        _git = git;
        _activeDocumentPath = activeDocumentPath;
        _solutionExplorer.SetRoots(Roots);
        _solutionExplorer.SetWorkspaceContext(null, hasWorkspace: false);
        _toolbar.SetProjects(Projects);
    }

    public ObservableCollection<FileNode> Roots { get; } = [];
    public ObservableCollection<FileNode> Projects { get; } = [];
    public string? WorkspacePath { get; private set; }
    public string? SolutionPath { get; private set; }

    public async Task LoadAsync(string path, string? selectedSolutionPath = null)
    {
        WorkspacePath = path;
        _toolbar.SetWorkspaceName(selectedSolutionPath is null ? Path.GetFileName(path) : Path.GetFileName(selectedSolutionPath));
        _properties.SetWorkspacePath(path);
        _output.StatusMessage = "Loading workspace...";
        Roots.Clear();
        Projects.Clear();
        var nodes = await _workspace.LoadAsync(path);
        var solution = nodes.FirstOrDefault(node =>
            (selectedSolutionPath is null || string.Equals(node.FullPath, selectedSolutionPath, StringComparison.OrdinalIgnoreCase)) &&
            (Path.GetExtension(node.Name).Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
             Path.GetExtension(node.Name).Equals(".slnx", StringComparison.OrdinalIgnoreCase)));
        SolutionPath = solution?.FullPath;
        _solutionExplorer.SetWorkspaceContext(SolutionPath, hasWorkspace: true);

        if (solution is not null)
        {
            var solutionRoot = await _workspace.LoadSolutionAsync(solution.FullPath);
            Roots.Add(solutionRoot);
            foreach (var project in solutionRoot.Children) Projects.Add(project);
            _toolbar.SetWorkspaceName(solution.Name);
        }
        else
        {
            var root = new FileNode { Name = Path.GetFileName(path), FullPath = path, IsDirectory = true };
            foreach (var node in nodes)
            {
                root.Children.Add(node);
                AddProjects(node);
            }
            Roots.Add(root);
        }

        _toolbar.SetStartupProject(Projects.FirstOrDefault());
        if (_activeDocumentPath() is { } activePath)
            _solutionExplorer.SelectAndReveal(activePath);
        _output.StatusMessage = "Ready";
        await _git.SetWorkspaceAsync(WorkspacePath);
    }

    private void AddProjects(FileNode node)
    {
        if (Path.GetExtension(node.Name).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            Projects.Add(node);
        foreach (var child in node.Children) AddProjects(child);
    }
}
